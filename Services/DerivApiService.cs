using System.Collections.Concurrent;
using System.Text.Json;
using Excalibur5.Config;
using Excalibur5.Models;

namespace Excalibur5.Services;

public sealed class DerivApiService : IDerivApiService, IDisposable
{
    private const string Src = "ApiService";

    private readonly IDerivWebSocketService _ws;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private int _reqId;

    public event EventHandler<AuthorizeResponse>? Authorized;
    public event EventHandler<BalanceResponse>?   BalanceUpdated;

    public DerivApiService(IDerivWebSocketService ws)
    {
        _ws = ws;
        _ws.MessageReceived += OnMessageReceived;
        AppLogger.Info(Src, "DerivApiService created");
    }

    private void OnMessageReceived(object? sender, string json)
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
                _pending.TryRemove(reqIdEl.GetInt32(), out var tcs))
            {
                tcs.TrySetResult(root.Clone());
            }

            if (root.TryGetProperty("msg_type", out var mt))
            {
                var msgType = mt.GetString();

                if (msgType == "balance" &&
                    root.TryGetProperty("balance", out var balEl))
                {
                    var balance = new BalanceResponse
                    {
                        Balance  = balEl.TryGetProperty("balance",  out var b) ? b.GetDecimal() : 0m,
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

    public async Task AuthorizeAsync(string token, CancellationToken ct = default)
    {
        var reqId = NextReqId();
        AppLogger.Info(Src, $"AuthorizeAsync req_id={reqId}");
        var json = JsonSerializer.Serialize(new { authorize = token, req_id = reqId });

        var root = await SendAndWaitAsync(reqId, json, ct);

        if (root.TryGetProperty("error", out var err))
        {
            var msg = err.GetProperty("message").GetString();
            AppLogger.Error(Src, $"Authorize error: {msg}");
            throw new InvalidOperationException(msg);
        }

        var auth = root.GetProperty("authorize");
        var response = new AuthorizeResponse
        {
            LoginId   = auth.TryGetProperty("loginid",    out var li)  ? li.GetString()  ?? "" : "",
            IsVirtual = auth.TryGetProperty("is_virtual", out var iv)  && iv.GetInt32() == 1,
            Balance   = auth.TryGetProperty("balance",    out var bal) ? bal.GetDecimal() : 0m,
            Currency  = auth.TryGetProperty("currency",   out var cur) ? cur.GetString()  ?? "" : "",
            FullName  = auth.TryGetProperty("fullname",   out var fn)  ? fn.GetString()   ?? "" : "",
        };
        AppLogger.Info(Src, $"Authorized: {response.LoginId} | virtual={response.IsVirtual} | {response.Balance} {response.Currency}");
        Authorized?.Invoke(this, response);
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
            var msg = err.GetProperty("message").GetString();
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

        await _ws.SendAsync(json, ct);
        return await tcs.Task;
    }

    private int NextReqId() => Interlocked.Increment(ref _reqId);

    public void Dispose()
    {
        _ws.MessageReceived -= OnMessageReceived;
        // Drain and cancel all pending requests atomically
        foreach (var key in _pending.Keys)
        {
            if (_pending.TryRemove(key, out var tcs))
                tcs.TrySetCanceled();
        }
        AppLogger.Info(Src, "DerivApiService disposed");
    }
}
