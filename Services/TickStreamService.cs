using System.Collections.Concurrent;
using System.Text.Json;
using Excalibur5.Models;

namespace Excalibur5.Services;

public sealed class TickStreamService : ITickStreamService, IDisposable
{
    private const string Src = "TickStream";

    private readonly IDerivWebSocketService _ws;
    private readonly ConcurrentDictionary<string, string> _subscriptions = new();
    private readonly ConcurrentDictionary<string, decimal> _lastQuotes = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private int _reqId = 10000;

    public event EventHandler<TickData>? TickReceived;

    public TickStreamService(IDerivWebSocketService ws)
    {
        _ws = ws;
        _ws.MessageReceived += OnMessage;
        AppLogger.Info(Src, "TickStreamService created");
    }

    private void OnMessage(object? sender, string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch { return; }

        using (doc)
        {
            var root = doc.RootElement;

            if (root.TryGetProperty("req_id", out var reqIdEl))
            {
                var rid = reqIdEl.GetInt32();
                if (_pending.TryRemove(rid, out var tcs))
                {
                    tcs.TrySetResult(root.Clone());
                }
            }

            if (root.TryGetProperty("msg_type", out var mt))
            {
                var msgType = mt.GetString();
                if (msgType == "tick" && root.TryGetProperty("tick", out var tickEl))
                {
                    ProcessTick(tickEl);
                }
            }
        }
    }

    private static readonly IReadOnlyDictionary<string, int> PipSizes =
        MarketInfo.SyntheticMarkets.ToDictionary(m => m.Symbol, m => m.PipSize);

    private void ProcessTick(JsonElement tickEl)
    {
        var symbol = tickEl.TryGetProperty("symbol", out var s) ? s.GetString() ?? "" : "";
        var epoch  = tickEl.TryGetProperty("epoch", out var e) ? e.GetInt64() : 0;

        string quoteRaw = "";
        decimal quote = 0m;
        if (tickEl.TryGetProperty("quote", out var q))
        {
            quoteRaw = q.GetRawText();
            quote = decimal.Parse(quoteRaw, System.Globalization.CultureInfo.InvariantCulture);

            if (PipSizes.TryGetValue(symbol, out var pipSize))
                quoteRaw = quote.ToString("F" + pipSize, System.Globalization.CultureInfo.InvariantCulture);
        }

        var direction = TickDirection.Flat;
        if (_lastQuotes.TryGetValue(symbol, out var prev))
        {
            if (quote > prev) direction = TickDirection.Up;
            else if (quote < prev) direction = TickDirection.Down;
        }
        _lastQuotes[symbol] = quote;

        var tick = new TickData
        {
            Symbol    = symbol,
            Quote     = quote,
            QuoteRaw  = quoteRaw,
            Epoch     = epoch,
            Direction = direction
        };

        TickReceived?.Invoke(this, tick);
    }

    public async Task<string> SubscribeAsync(string symbol, CancellationToken ct = default)
    {
        if (_subscriptions.ContainsKey(symbol))
        {
            AppLogger.Info(Src, $"Already subscribed to {symbol}");
            return _subscriptions[symbol];
        }

        var reqId = Interlocked.Increment(ref _reqId);
        var payload = JsonSerializer.Serialize(new { ticks = symbol, subscribe = 1, req_id = reqId });

        _subscriptions[symbol] = "";

        try
        {
            var root = await SendAndWaitAsync(reqId, payload, ct, timeoutMs: 10000);

            if (root.TryGetProperty("error", out var err))
            {
                var msg = err.TryGetProperty("message", out var m) ? m.GetString() : "unknown";

                if (msg != null && msg.Contains("already subscribed", StringComparison.OrdinalIgnoreCase))
                {
                    AppLogger.Warn(Src, $"Server says already subscribed to {symbol} — forget_all and retry");
                    _subscriptions.TryRemove(symbol, out _);
                    await ForgetAllTicksAsync(ct);
                    return await SubscribeAsync(symbol, ct);
                }

                _subscriptions.TryRemove(symbol, out _);
                AppLogger.Error(Src, $"Subscribe error for {symbol}: {msg}");
                throw new InvalidOperationException(msg);
            }

            var subId = "";
            if (root.TryGetProperty("subscription", out var sub) &&
                sub.TryGetProperty("id", out var idEl))
            {
                subId = idEl.GetString() ?? "";
            }

            _subscriptions[symbol] = subId;
            AppLogger.Info(Src, $"Subscribed to {symbol}, sub_id={subId}");
            return subId;
        }
        catch (OperationCanceledException)
        {
            AppLogger.Warn(Src, $"Subscribe response timeout for {symbol} — ticks may still arrive");
            return "";
        }
    }

    public async Task UnsubscribeAsync(string symbol, CancellationToken ct = default)
    {
        if (!_subscriptions.TryRemove(symbol, out var subId) || string.IsNullOrEmpty(subId))
            return;

        _lastQuotes.TryRemove(symbol, out _);

        var reqId = Interlocked.Increment(ref _reqId);
        var payload = JsonSerializer.Serialize(new { forget = subId, req_id = reqId });

        try
        {
            await SendAndWaitAsync(reqId, payload, ct);
            AppLogger.Info(Src, $"Unsubscribed from {symbol}");
        }
        catch (Exception ex)
        {
            AppLogger.Warn(Src, $"Unsubscribe error for {symbol}: {ex.Message}");
        }
    }

    public async Task UnsubscribeAllAsync(CancellationToken ct = default)
    {
        var symbols = _subscriptions.Keys.ToList();
        foreach (var symbol in symbols)
            await UnsubscribeAsync(symbol, ct);
    }

    public async Task ForgetAllTicksAsync(CancellationToken ct = default)
    {
        var reqId = Interlocked.Increment(ref _reqId);
        var payload = JsonSerializer.Serialize(new { forget_all = "ticks", req_id = reqId });

        try
        {
            await SendAndWaitAsync(reqId, payload, ct);
            _subscriptions.Clear();
            _lastQuotes.Clear();
            AppLogger.Info(Src, "forget_all ticks sent — local state cleared");
        }
        catch (Exception ex)
        {
            AppLogger.Warn(Src, $"forget_all ticks failed: {ex.Message}");
        }
    }

    public void ClearSubscription(string symbol)
    {
        _subscriptions.TryRemove(symbol, out _);
        _lastQuotes.TryRemove(symbol, out _);
        AppLogger.Info(Src, $"Cleared local subscription state for {symbol}");
    }

    public async Task<List<TickData>> GetHistoryAsync(string symbol, int count = 1000, CancellationToken ct = default)
    {
        var reqId = Interlocked.Increment(ref _reqId);
        var payload = JsonSerializer.Serialize(new { ticks_history = symbol, count, end = "latest", style = "ticks", req_id = reqId });

        var root = await SendAndWaitAsync(reqId, payload, ct);

        if (root.TryGetProperty("error", out var err))
        {
            var msg = err.TryGetProperty("message", out var m) ? m.GetString() : "unknown";
            AppLogger.Error(Src, $"History error for {symbol}: {msg}");
            return new List<TickData>();
        }

        var result = new List<TickData>();
        if (!root.TryGetProperty("history", out var history))
            return result;

        var prices = history.TryGetProperty("prices", out var p) ? p : default;
        var times  = history.TryGetProperty("times", out var t) ? t : default;

        if (prices.ValueKind != JsonValueKind.Array)
            return result;

        var pipSize = PipSizes.TryGetValue(symbol, out var ps) ? ps : 2;
        decimal prevQuote = 0m;

        for (int i = 0; i < prices.GetArrayLength(); i++)
        {
            var rawPrice = prices[i].GetRawText();
            var quote = decimal.Parse(rawPrice, System.Globalization.CultureInfo.InvariantCulture);
            var quoteFormatted = quote.ToString("F" + pipSize, System.Globalization.CultureInfo.InvariantCulture);
            var epoch = times.ValueKind == JsonValueKind.Array && i < times.GetArrayLength()
                ? times[i].GetInt64() : 0;

            var direction = TickDirection.Flat;
            if (i > 0)
            {
                if (quote > prevQuote) direction = TickDirection.Up;
                else if (quote < prevQuote) direction = TickDirection.Down;
            }
            prevQuote = quote;

            result.Add(new TickData
            {
                Symbol    = symbol,
                Quote     = quote,
                QuoteRaw  = quoteFormatted,
                Epoch     = epoch,
                Direction = direction
            });
        }

        _lastQuotes[symbol] = prevQuote;
        AppLogger.Info(Src, $"History loaded for {symbol}: {result.Count} ticks");
        return result;
    }

    public async Task<List<CandleData>> GetCandleHistoryAsync(string symbol, int granularity = 60, int count = 1000, CancellationToken ct = default)
    {
        var reqId = Interlocked.Increment(ref _reqId);
        var payload = JsonSerializer.Serialize(new { ticks_history = symbol, count, end = "latest", style = "candles", granularity, req_id = reqId });

        var root = await SendAndWaitAsync(reqId, payload, ct);

        if (root.TryGetProperty("error", out var err))
        {
            var msg = err.TryGetProperty("message", out var m) ? m.GetString() : "unknown";
            AppLogger.Error(Src, $"Candle history error for {symbol}: {msg}");
            return new List<CandleData>();
        }

        var result = new List<CandleData>();
        if (!root.TryGetProperty("candles", out var candles) || candles.ValueKind != JsonValueKind.Array)
            return result;

        for (int i = 0; i < candles.GetArrayLength(); i++)
        {
            var c = candles[i];
            var epoch = c.TryGetProperty("epoch", out var ep) ? ep.GetInt64() : 0;
            var open  = c.TryGetProperty("open", out var o) ? decimal.Parse(o.GetRawText(), System.Globalization.CultureInfo.InvariantCulture) : 0m;
            var high  = c.TryGetProperty("high", out var h) ? decimal.Parse(h.GetRawText(), System.Globalization.CultureInfo.InvariantCulture) : 0m;
            var low   = c.TryGetProperty("low", out var l) ? decimal.Parse(l.GetRawText(), System.Globalization.CultureInfo.InvariantCulture) : 0m;
            var close = c.TryGetProperty("close", out var cl) ? decimal.Parse(cl.GetRawText(), System.Globalization.CultureInfo.InvariantCulture) : 0m;

            result.Add(new CandleData { Epoch = epoch, Open = open, High = high, Low = low, Close = close });
        }

        AppLogger.Info(Src, $"Candle history loaded for {symbol}: {result.Count} candles (granularity={granularity}s)");
        return result;
    }

    private async Task<JsonElement> SendAndWaitAsync(int reqId, string json, CancellationToken ct, int timeoutMs = 15000)
    {
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[reqId] = tcs;

        using var timeout = new CancellationTokenSource(timeoutMs);
        using var linked  = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        using var reg     = linked.Token.Register(() =>
        {
            if (_pending.TryRemove(reqId, out var t))
                t.TrySetCanceled();
        });

        await _ws.SendAsync(json, ct);
        return await tcs.Task;
    }

    public void Dispose()
    {
        _ws.MessageReceived -= OnMessage;
        foreach (var key in _pending.Keys)
        {
            if (_pending.TryRemove(key, out var tcs))
                tcs.TrySetCanceled();
        }
        AppLogger.Info(Src, "TickStreamService disposed");
    }
}
