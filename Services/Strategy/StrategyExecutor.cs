using Excalibur5.Models;
using Excalibur5.Models.Strategy;
using Excalibur5.Services;

namespace Excalibur5.Services.Strategy;

public sealed class StrategyExecutor : IDisposable
{
    private const string Src = "StrategyExecutor";

    private readonly IContractService _contractService;
    private readonly IStrategyEngine _engine;
    private readonly Dictionary<long, TrackedPosition> _positions = new();
    private StrategyConfig _config = new();
    private string _symbol = string.Empty;
    private bool _active;
    private int _buyingCount;
    private int _martingaleLevel;

    public StrategyStats Stats { get; } = new();
    public int ActivePositionCount => _positions.Count;

    public event EventHandler? StatsUpdated;
    public event EventHandler<string>? TradeExecuted;
    public event EventHandler<BotPositionOpened>? PositionOpened;

    public StrategyExecutor(IContractService contractService, IStrategyEngine engine)
    {
        _contractService = contractService;
        _engine = engine;
        _engine.SignalGenerated += OnSignalGenerated;
        _contractService.OpenContractUpdated += OnOpenContractUpdated;
    }

    public void Start(StrategyConfig config, string symbol)
    {
        _config = config;
        _symbol = symbol;
        _active = true;
        _buyingCount = 0;
        _martingaleLevel = 0;
        AppLogger.Info(Src, $"Executor started for {symbol}, TP={config.TakeProfitUsd}, SL={config.StopLossUsd}, trailing={config.EnableTrailingStop}, recover={config.RecoverMode}");
    }

    public void Stop()
    {
        _active = false;
        AppLogger.Info(Src, "Executor stopped");
    }

    private async void OnSignalGenerated(object? sender, TradeSignal signal)
    {
        if (!_active) return;

        if (_config.StrategyMode == "Tendência" && HasExpiredUnresolvedPositions())
        {
            await Task.Delay(2000);
            if (!_active) return;
            ResolveStaleExpiredPositions();
        }

        var activeCount = CountActivePositions();
        var currentBuying = Interlocked.Increment(ref _buyingCount);
        if (activeCount + currentBuying > _config.MaxConcurrentContracts)
        {
            Interlocked.Decrement(ref _buyingCount);
            AppLogger.Info(Src, $"Signal ignored — max contracts reached ({activeCount}/{_config.MaxConcurrentContracts})");
            return;
        }

        try
        {
            string contractType = signal.Direction == SignalDirection.Call
                ? "VANILLALONGCALL"
                : "VANILLALONGPUT";

            var stake = GetCurrentStake();

            var result = await _contractService.BuyDirectAsync(
                _symbol,
                contractType,
                stake,
                _config.DurationSeconds,
                "s",
                _config.Barrier);

            if (result.ContractId > 0)
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var tracked = new TrackedPosition
                {
                    ContractId = result.ContractId,
                    Direction = signal.Direction,
                    BuyPrice = result.BuyPrice,
                    Signal = signal,
                    DynamicStopLoss = -_config.StopLossUsd,
                    EntryEpoch = now,
                    ExpiryEpoch = now + _config.DurationSeconds
                };
                lock (_positions)
                    _positions[result.ContractId] = tracked;

                await _contractService.SubscribeOpenContractAsync(result.ContractId);

                var dirLabel = signal.Direction == SignalDirection.Call ? "CALL" : "PUT";
                TradeExecuted?.Invoke(this, $"Comprou {dirLabel} — {signal.Reason}");
                AppLogger.Info(Src, $"Bought {dirLabel} contract {result.ContractId}, stake={stake}");
                PositionOpened?.Invoke(this, new BotPositionOpened(result, contractType));
            }
            else
            {
                AppLogger.Warn(Src, $"Buy returned no contract ID: {result.Error}");
                TradeExecuted?.Invoke(this, $"Erro: {result.Error}");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn(Src, $"Buy failed: {ex.Message}");
            TradeExecuted?.Invoke(this, $"Erro: {ex.Message}");
        }
        finally
        {
            Interlocked.Decrement(ref _buyingCount);
        }
    }

    private async void OnOpenContractUpdated(object? sender, OpenContractUpdate update)
    {
        TrackedPosition? tracked;
        lock (_positions)
        {
            if (!_positions.TryGetValue(update.ContractId, out tracked))
                return;
            if (tracked.IsSelling)
                return;
        }

        if (!_active) return;

        if (update.IsExpired || update.IsSold || update.Status is "sold" or "won" or "lost")
        {
            bool won = update.Profit >= 0;
            _engine.RecordTradeResult(tracked.Signal.ContributingIndicators, won);
            RecordResult(tracked, update.Profit);
            RemovePosition(update.ContractId);
            return;
        }

        // Trailing stop logic
        if (_config.EnableTrailingStop && update.Profit > 0)
        {
            decimal tp = _config.TakeProfitUsd;

            if (update.Profit >= tp * 0.9m)
            {
                decimal newSl = tp * 0.5m;
                if (newSl > tracked.DynamicStopLoss)
                {
                    tracked.DynamicStopLoss = newSl;
                    AppLogger.Info(Src, $"Trailing SL → +{newSl:F2} for {update.ContractId}");
                }
            }
            else if (update.Profit >= tp * 0.7m)
            {
                if (tracked.DynamicStopLoss < 0)
                {
                    tracked.DynamicStopLoss = 0;
                    AppLogger.Info(Src, $"Trailing SL → breakeven for {update.ContractId}");
                }
            }
        }

        // Check Take Profit
        if (update.Profit >= _config.TakeProfitUsd)
        {
            if (!update.IsValidToSell) return;
            tracked.IsSelling = true;
            AppLogger.Info(Src, $"TP hit for {update.ContractId}: profit={update.Profit:F2} >= {_config.TakeProfitUsd}");
            await SellPositionAsync(update.ContractId, tracked, update.Profit);
            return;
        }

        // Time-based dynamic stop loss
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var totalDuration = tracked.ExpiryEpoch - tracked.EntryEpoch;
        var timeRemaining = Math.Max(tracked.ExpiryEpoch - now, 1);
        var timeRatio = totalDuration > 0 ? (decimal)timeRemaining / totalDuration : 0m;
        var timeSl = -(_config.StopLossUsd * timeRatio);

        // Use the tighter of trailing SL and time-based SL
        decimal effectiveSl = _config.EnableTrailingStop
            ? Math.Max(tracked.DynamicStopLoss, timeSl)
            : timeSl;

        if (update.Profit <= effectiveSl)
        {
            if (!update.IsValidToSell) return;
            tracked.IsSelling = true;
            AppLogger.Info(Src, $"SL hit for {update.ContractId}: profit={update.Profit:F2} <= {effectiveSl:F2} (time ratio={timeRatio:F2})");
            await SellPositionAsync(update.ContractId, tracked, update.Profit);
        }
    }

    private async Task SellPositionAsync(long contractId, TrackedPosition tracked, decimal profit)
    {
        try
        {
            var result = await _contractService.SellContractAsync(contractId);
            if (result.Success)
            {
                bool won = profit >= 0;
                _engine.RecordTradeResult(tracked.Signal.ContributingIndicators, won);
                RecordResult(tracked, profit);
                RemovePosition(contractId);
                AppLogger.Info(Src, $"Sold contract {contractId}, profit={profit:F2}");
            }
            else
            {
                AppLogger.Warn(Src, $"Sell failed for {contractId}: {result.Error}");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn(Src, $"Sell error for {contractId}: {ex.Message}");
        }
    }

    private void RecordResult(TrackedPosition tracked, decimal profit)
    {
        if (profit >= 0)
        {
            Stats.RecordWin(profit);
            _martingaleLevel = 0;
        }
        else
        {
            Stats.RecordLoss(profit);
            if (_config.RecoverMode == "Martingale" && _martingaleLevel < _config.MartingaleMaxLevel)
                _martingaleLevel++;
            else
                _martingaleLevel = 0;
        }

        StatsUpdated?.Invoke(this, EventArgs.Empty);
    }

    private decimal GetCurrentStake()
    {
        if (_config.RecoverMode != "Martingale" || _martingaleLevel <= 0)
            return _config.Stake;

        var stake = _config.Stake;
        for (int i = 0; i < _martingaleLevel; i++)
            stake *= _config.MartingaleFactor;
        return Math.Round(stake, 2);
    }

    private void RemovePosition(long contractId)
    {
        lock (_positions)
            _positions.Remove(contractId);
    }

    private void ResolveStaleExpiredPositions()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        List<TrackedPosition>? stale = null;
        lock (_positions)
        {
            foreach (var kvp in _positions)
            {
                if (kvp.Value.ExpiryEpoch <= now && !kvp.Value.IsSelling)
                {
                    stale ??= new List<TrackedPosition>();
                    stale.Add(kvp.Value);
                }
            }
            if (stale != null)
            {
                foreach (var pos in stale)
                    _positions.Remove(pos.ContractId);
            }
        }

        if (stale != null)
        {
            foreach (var pos in stale)
            {
                _engine.RecordTradeResult(pos.Signal.ContributingIndicators, false);
                RecordResult(pos, -pos.BuyPrice);
                AppLogger.Info(Src, $"Resolved stale position {pos.ContractId} as loss (martingale level={_martingaleLevel})");
            }
        }
    }

    private bool HasExpiredUnresolvedPositions()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        lock (_positions)
        {
            foreach (var kvp in _positions)
            {
                if (kvp.Value.ExpiryEpoch <= now)
                    return true;
            }
        }
        return false;
    }

    private int CountActivePositions()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        lock (_positions)
        {
            int count = 0;
            foreach (var kvp in _positions)
            {
                if (kvp.Value.ExpiryEpoch > now)
                    count++;
            }
            return count;
        }
    }

    public void Dispose()
    {
        _engine.SignalGenerated -= OnSignalGenerated;
        _contractService.OpenContractUpdated -= OnOpenContractUpdated;
    }

    private sealed class TrackedPosition
    {
        public long ContractId { get; init; }
        public SignalDirection Direction { get; init; }
        public decimal BuyPrice { get; init; }
        public TradeSignal Signal { get; init; } = null!;
        public decimal DynamicStopLoss { get; set; }
        public long EntryEpoch { get; init; }
        public long ExpiryEpoch { get; init; }
        public bool IsSelling { get; set; }
    }
}

public sealed record BotPositionOpened(BuyResponse BuyResult, string ContractType);
