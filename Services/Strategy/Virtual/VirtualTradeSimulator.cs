using Excalibur5.Models.Strategy;

namespace Excalibur5.Services.Strategy.Virtual;

public interface IVirtualTradeSimulator : IDisposable
{
    bool HasActiveTrade { get; }
    event EventHandler<VirtualTradeCompleted>? TradeCompleted;
    event EventHandler<VirtualTradeUpdated>? TradeUpdated;
    bool TryStart(VirtualTradeRequest request);
    void UpdateSpot(decimal spot);
    void Pause();
    void Resume();
    void Cancel();
}

public sealed record VirtualTradeRequest(
    long TradeId,
    TradeSignal Signal,
    decimal EntrySpot,
    int DurationSeconds,
    int DurationTicks,
    decimal WinProfit);

public sealed record VirtualTradeCompleted(long TradeId, TradeSignal Signal, bool Won, decimal ExitSpot, decimal WinProfit);
public sealed record VirtualTradeUpdated(long TradeId, decimal CurrentSpot);

public sealed class VirtualTradeSimulator : IVirtualTradeSimulator
{
    private readonly object _sync = new();
    private ActiveVirtualTrade? _activeTrade;
    private Timer? _timer;
    private bool _paused;

    public bool HasActiveTrade
    {
        get
        {
            lock (_sync)
                return _activeTrade != null;
        }
    }

    public event EventHandler<VirtualTradeCompleted>? TradeCompleted;
    public event EventHandler<VirtualTradeUpdated>? TradeUpdated;

    public bool TryStart(VirtualTradeRequest request)
    {
        lock (_sync)
        {
            if (_activeTrade != null || request.EntrySpot <= 0) return false;

            _paused = false;
            _activeTrade = CreateTrade(request);
            StartTimerIfNeeded();
            return true;
        }
    }

    public void UpdateSpot(decimal spot)
    {
        VirtualTradeCompleted? completed = null;
        VirtualTradeUpdated? updated = null;
        lock (_sync)
        {
            if (_activeTrade == null || _paused || spot <= 0) return;

            _activeTrade.LastSpot = spot;
            updated = new VirtualTradeUpdated(_activeTrade.TradeId, spot);
            if (_activeTrade.DurationTicks > 0)
            {
                _activeTrade.TicksObserved++;
                if (_activeTrade.TicksObserved >= _activeTrade.DurationTicks)
                    completed = CompleteTrade();
            }
        }

        if (updated != null)
            TradeUpdated?.Invoke(this, updated);
        RaiseCompleted(completed);
    }

    public void Pause()
    {
        lock (_sync)
        {
            if (_activeTrade == null || _paused) return;

            _paused = true;
            if (_activeTrade.DurationTicks > 0) return;

            _activeTrade.Remaining = Max(
                _activeTrade.Deadline - DateTimeOffset.UtcNow,
                TimeSpan.Zero);
            _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    public void Resume()
    {
        lock (_sync)
        {
            if (_activeTrade == null || !_paused) return;

            _paused = false;
            if (_activeTrade.DurationTicks > 0) return;

            _activeTrade.Deadline = DateTimeOffset.UtcNow + _activeTrade.Remaining;
            _timer?.Change(_activeTrade.Remaining, Timeout.InfiniteTimeSpan);
        }
    }

    public void Cancel()
    {
        lock (_sync)
        {
            _activeTrade = null;
            _paused = false;
            _timer?.Dispose();
            _timer = null;
        }
    }

    public void Dispose() => Cancel();

    private static ActiveVirtualTrade CreateTrade(VirtualTradeRequest request)
    {
        var duration = TimeSpan.FromSeconds(Math.Max(1, request.DurationSeconds));
        return new ActiveVirtualTrade
        {
            TradeId = request.TradeId,
            Signal = request.Signal,
            EntrySpot = request.EntrySpot,
            LastSpot = request.EntrySpot,
            DurationTicks = Math.Max(0, request.DurationTicks),
            WinProfit = request.WinProfit,
            Remaining = duration,
            Deadline = DateTimeOffset.UtcNow + duration
        };
    }

    private void StartTimerIfNeeded()
    {
        if (_activeTrade == null || _activeTrade.DurationTicks > 0) return;

        _timer = new Timer(OnTimerElapsed, null, _activeTrade.Remaining, Timeout.InfiniteTimeSpan);
    }

    private void OnTimerElapsed(object? state)
    {
        VirtualTradeCompleted? completed;
        lock (_sync)
        {
            if (_activeTrade == null || _paused) return;
            completed = CompleteTrade();
        }

        RaiseCompleted(completed);
    }

    private VirtualTradeCompleted? CompleteTrade()
    {
        if (_activeTrade == null) return null;

        var trade = _activeTrade;
        _activeTrade = null;
        _timer?.Dispose();
        _timer = null;

        var won = trade.Signal.Direction == SignalDirection.Call
            ? trade.LastSpot > trade.EntrySpot
            : trade.LastSpot < trade.EntrySpot;
        return new VirtualTradeCompleted(trade.TradeId, trade.Signal, won, trade.LastSpot, trade.WinProfit);
    }

    private void RaiseCompleted(VirtualTradeCompleted? completed)
    {
        if (completed != null)
            TradeCompleted?.Invoke(this, completed);
    }

    private static TimeSpan Max(TimeSpan left, TimeSpan right) => left > right ? left : right;

    private sealed class ActiveVirtualTrade
    {
        public long TradeId { get; init; }
        public TradeSignal Signal { get; init; } = null!;
        public decimal EntrySpot { get; init; }
        public decimal LastSpot { get; set; }
        public int DurationTicks { get; init; }
        public decimal WinProfit { get; init; }
        public int TicksObserved { get; set; }
        public TimeSpan Remaining { get; set; }
        public DateTimeOffset Deadline { get; set; }
    }
}
