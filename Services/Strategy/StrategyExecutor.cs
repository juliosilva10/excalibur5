using Excalibur5.Models;
using Excalibur5.Models.Strategy;
using Excalibur5.Services;

namespace Excalibur5.Services.Strategy;

/// <summary>
/// Executes trades based on signals from the StrategyEngine.
/// Monitors open positions and auto-sells at TP/SL.
/// </summary>
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

    public StrategyStats Stats { get; } = new();
    public int ActivePositionCount => _positions.Count;

    public event EventHandler? StatsUpdated;
    public event EventHandler<string>? TradeExecuted;

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
        AppLogger.Info(Src, $"Executor started for {symbol}, TP={config.TakeProfitUsd}, SL={config.StopLossUsd}");
    }

    public void Stop()
    {
        _active = false;
        AppLogger.Info(Src, "Executor stopped");
    }

    private async void OnSignalGenerated(object? sender, TradeSignal signal)
    {
        if (!_active) return;
        if (_positions.Count + _buyingCount >= _config.MaxConcurrentContracts)
        {
            AppLogger.Info(Src, $"Signal ignored — max contracts reached ({_positions.Count}/{_config.MaxConcurrentContracts})");
            return;
        }

        Interlocked.Increment(ref _buyingCount);
        try
        {
            string contractType = signal.Direction == SignalDirection.Call
                ? "VANILLALONGCALL"
                : "VANILLALONGPUT";

            var result = await _contractService.BuyDirectAsync(
                _symbol,
                contractType,
                _config.Stake,
                _config.DurationMinutes,
                "m"); // minutes

            if (result.ContractId > 0)
            {
                var tracked = new TrackedPosition
                {
                    ContractId = result.ContractId,
                    Direction = signal.Direction,
                    BuyPrice = result.BuyPrice,
                    Signal = signal
                };
                lock (_positions)
                    _positions[result.ContractId] = tracked;

                await _contractService.SubscribeOpenContractAsync(result.ContractId);

                var dirLabel = signal.Direction == SignalDirection.Call ? "CALL" : "PUT";
                TradeExecuted?.Invoke(this, $"Comprou {dirLabel} — {signal.Reason}");
                AppLogger.Info(Src, $"Bought {dirLabel} contract {result.ContractId}, stake={_config.Stake}");
            }
            else
            {
                AppLogger.Warn(Src, "Buy returned no contract ID");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn(Src, $"Buy failed: {ex.Message}");
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
        }

        if (!_active) return;

        // Check if contract ended naturally
        if (update.IsExpired || update.IsSold || update.Status is "sold" or "won" or "lost")
        {
            RecordResult(tracked, update.Profit);
            RemovePosition(update.ContractId);
            return;
        }

        // Check Take Profit
        if (update.Profit >= _config.TakeProfitUsd)
        {
            AppLogger.Info(Src, $"TP hit for {update.ContractId}: profit={update.Profit:F2} >= {_config.TakeProfitUsd}");
            await SellPositionAsync(update.ContractId, tracked, update.Profit);
            return;
        }

        // Check Stop Loss (profit is negative)
        if (update.Profit <= -_config.StopLossUsd)
        {
            AppLogger.Info(Src, $"SL hit for {update.ContractId}: loss={update.Profit:F2} <= -{_config.StopLossUsd}");
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
            Stats.RecordWin(profit);
        else
            Stats.RecordLoss(profit);

        StatsUpdated?.Invoke(this, EventArgs.Empty);
    }

    private void RemovePosition(long contractId)
    {
        lock (_positions)
            _positions.Remove(contractId);
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
    }
}
