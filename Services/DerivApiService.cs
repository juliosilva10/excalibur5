using System.Collections.Concurrent;
using System.Text.Json;
using Excalibur5.Config;
using Excalibur5.Models;

namespace Excalibur5.Services;

public sealed class DerivApiService : IDerivApiService, IDisposable
{
    private const string Src = "ApiService";

    private readonly IDerivWebSocketService _ws;
    private readonly IDerivRestClient       _rest;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private int _reqId;

    public event EventHandler<AuthorizeResponse>? Authorized;
    public event EventHandler<BalanceResponse>?   BalanceUpdated;

    public DerivApiService(IDerivWebSocketService ws, IDerivRestClient rest)
    {
        _ws   = ws;
        _rest = rest;
        _ws.MessageReceived += OnMessageReceived;
        AppLogger.Info(Src, "DerivApiService created (Options platform)");
    }

    private void OnMessageReceived(object? sender, string json)
    {
        try
        {
            JsonDocument doc;
            try { doc = JsonDocument.Parse(json); }
            catch (Exception ex)
            {
                AppLogger.Error(Src, "Failed to parse incoming JSON", ex);
                return;
            }

            using (doc)
            {
                var root = doc.RootElement;

                if (root.TryGetProperty("req_id", out var reqIdEl) &&
                    reqIdEl.TryGetInt32(out var reqId) &&
                    _pending.TryRemove(reqId, out var tcs))
                {
                    tcs.TrySetResult(root.Clone());
                }

                if (root.TryGetProperty("msg_type", out var mt))
                {
                    var msgType = mt.GetString();

                    // The OTP-authenticated connection may return an "authorize" msg_type
                    // with account info on connect (new platform behaviour).
                    if (msgType == "authorize" &&
                        root.TryGetProperty("authorize", out var authEl))
                    {
                        var isVirtual = false;
                        if (authEl.TryGetProperty("is_virtual", out var iv))
                        {
                            isVirtual = iv.ValueKind == JsonValueKind.True
                                || (iv.ValueKind == JsonValueKind.Number && iv.TryGetInt32(out var ivNum) && ivNum == 1);
                        }
                        var response = new AuthorizeResponse
                        {
                            LoginId   = authEl.TryGetProperty("loginid",    out var li)  ? li.GetString()  ?? "" : "",
                            IsVirtual = isVirtual,
                            Balance   = authEl.TryGetProperty("balance",    out var bal) && bal.TryGetDecimal(out var balVal) ? balVal : 0m,
                            Currency  = authEl.TryGetProperty("currency",   out var cur) ? cur.GetString()  ?? "" : "",
                            FullName  = authEl.TryGetProperty("fullname",   out var fn)  ? fn.GetString()   ?? "" : "",
                        };
                        AppLogger.Info(Src, $"Authorized (WS): {MaskLoginId(response.LoginId)} | virtual={response.IsVirtual} | {response.Balance} {response.Currency}");
                        Authorized?.Invoke(this, response);
                    }

                    if (msgType == "balance" &&
                        root.TryGetProperty("balance", out var balEl))
                    {
                        var balance = new BalanceResponse
                        {
                            Balance  = balEl.TryGetProperty("balance",  out var b) && b.TryGetDecimal(out var bVal) ? bVal : 0m,
                            Currency = balEl.TryGetProperty("currency", out var c) ? c.GetString() ?? "" : "",
                            LoginId  = balEl.TryGetProperty("loginid",  out var l) ? l.GetString() ?? "" : "",
                        };
                        AppLogger.Info(Src, $"Balance update: {balance.Balance} {balance.Currency}");
                        BalanceUpdated?.Invoke(this, balance);
                    }

                    if (msgType == "error")
                    {
                        var errMsg = root.TryGetProperty("error", out var e)
                            ? e.TryGetProperty("message", out var m) ? m.GetString() : "unknown"
                            : "unknown";
                        AppLogger.Warn(Src, $"Server error push (no req_id): {errMsg}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // A malformed payload must never tear down the shared WebSocket receive loop.
            AppLogger.Warn(Src, $"OnMessageReceived error (ignored): {ex.Message}");
        }
    }

    // Masks a login id for logging (keeps first 2 + last 2 chars), so PII isn't written in plaintext.
    private static string MaskLoginId(string loginId)
    {
        if (string.IsNullOrEmpty(loginId) || loginId.Length <= 4)
            return "****";
        return $"{loginId[..2]}****{loginId[^2..]}";
    }

    /// <inheritdoc />
    public async Task ConnectAndAuthorizeAsync(string patToken, CancellationToken ct = default)
    {
        AppLogger.Info(Src, "ConnectAndAuthorizeAsync — REST account discovery + OTP flow");

        // 1. REST: discover account ID and type
        var (accountId, accountType) = await _rest.GetAccountIdAsync(patToken, ct);

        // 2. REST: get OTP-authenticated WebSocket URL
        var wsUrl = await _rest.GetWebSocketUrlAsync(accountId, patToken, ct);

        // 3. Connect WebSocket (already authenticated — no authorize message needed)
        await _ws.ConnectAsync(new Uri(wsUrl), ct);

        // 4. Fire Authorized event from REST data (account_type is authoritative for the Options platform)
        var restAuth = new AuthorizeResponse
        {
            LoginId   = accountId,
            IsVirtual = string.Equals(accountType, "demo", StringComparison.OrdinalIgnoreCase),
        };
        AppLogger.Info(Src, $"Authorized (REST): {restAuth.LoginId} | virtual={restAuth.IsVirtual} | type={accountType}");
        Authorized?.Invoke(this, restAuth);

        // 5. Subscribe to balance updates (will update balance/currency via BalanceUpdated event)
        await SubscribeBalanceAsync(ct);
    }

    public async Task<long> PingAsync(CancellationToken ct = default)
    {
        var reqId = NextReqId();
        var json  = JsonSerializer.Serialize(new { ping = 1, req_id = reqId });
        var sw    = System.Diagnostics.Stopwatch.StartNew();
        await SendAndWaitAsync(reqId, json, ct);
        sw.Stop();
        return sw.ElapsedMilliseconds;
    }

    public async Task<DateTimeOffset> GetServerTimeAsync(CancellationToken ct = default)
    {
        var reqId = NextReqId();
        var json  = JsonSerializer.Serialize(new { time = 1, req_id = reqId });
        var root  = await SendAndWaitAsync(reqId, json, ct);
        if (root.TryGetProperty("time", out var t))
            return DateTimeOffset.FromUnixTimeSeconds(t.GetInt64());
        return DateTimeOffset.UtcNow;
    }

    public async Task SubscribeBalanceAsync(CancellationToken ct = default)
    {
        var reqId = NextReqId();
        AppLogger.Info(Src, $"SubscribeBalanceAsync req_id={reqId}");
        var json  = JsonSerializer.Serialize(new { balance = 1, subscribe = 1, req_id = reqId });
        var root  = await SendAndWaitAsync(reqId, json, ct);

        if (root.TryGetProperty("error", out var err))
        {
            var msg = err.TryGetProperty("message", out var m) ? m.GetString() ?? "unknown" : "unknown";
            AppLogger.Error(Src, $"SubscribeBalance error: {msg}");
            throw new InvalidOperationException(msg);
        }

        if (root.TryGetProperty("subscription", out var sub) &&
            sub.TryGetProperty("id", out var subId))
        {
            AppLogger.Info(Src, $"Balance subscription id: {subId.GetString()}");
        }
    }

    private async Task<JsonElement> SendAndWaitAsync(int reqId, string json, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[reqId] = tcs;

        using var timeout = new CancellationTokenSource(AppConfig.RequestTimeoutMs);
        using var linked  = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        using var reg     = linked.Token.Register(() =>
        {
            if (_pending.TryRemove(reqId, out var t))
            {
                AppLogger.Warn(Src, $"Request req_id={reqId} timed out or cancelled");
                t.TrySetCanceled();
            }
        });

        try
        {
            await _ws.SendAsync(json, ct);
        }
        catch
        {
            if (_pending.TryRemove(reqId, out var orphan))
                orphan.TrySetCanceled();
            throw;
        }

        return await tcs.Task;
    }

    private int NextReqId() => Interlocked.Increment(ref _reqId);

    public void Dispose()
    {
        _ws.MessageReceived -= OnMessageReceived;
        // Note: _rest is an injected dependency; its lifetime is owned by the DI container, not this service.
        // Drain and cancel all pending requests atomically
        foreach (var key in _pending.Keys)
        {
            if (_pending.TryRemove(key, out var tcs))
                tcs.TrySetCanceled();
        }
        AppLogger.Info(Src, "DerivApiService disposed");
    }
}
