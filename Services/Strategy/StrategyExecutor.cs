using Excalibur5.Models;
using Excalibur5.Models.Strategy;
using Excalibur5.Services;
using Excalibur5.Services.Strategy.Recovery;

namespace Excalibur5.Services.Strategy;

public sealed class StrategyExecutor : IDisposable
{
    private const string Src = "StrategyExecutor";
    private const string BotCallKey = "BOT_CALL";
    private const string BotPutKey = "BOT_PUT";

    private readonly IContractService _contractService;
    private readonly IStrategyEngine _engine;
    private readonly Dictionary<long, TrackedPosition> _positions = new();
    private readonly SemaphoreSlim _proposalLock = new(1, 1);
    private readonly SemaphoreSlim _signalLock = new(1, 1);
    private CancellationTokenSource _proposalCts = new();
    private StrategyConfig _config = new();
    private string _symbol = string.Empty;
    private bool _active;
    private IRecoverStrategy? _recoverStrategy;
    private decimal _currentSpot;

    // Pre-subscribed proposal state
    private string _callProposalId = string.Empty;
    private string _callSubscriptionId = string.Empty;
    private decimal _callAskPrice;
    private string _putProposalId = string.Empty;
    private string _putSubscriptionId = string.Empty;
    private decimal _putAskPrice;
    private bool _proposalsReady;
    private decimal _proposalStake;

    public StrategyStats Stats { get; } = new();
    public int ActivePositionCount => _positions.Count;

    public event EventHandler? StatsUpdated;
    public event EventHandler<string>? TradeExecuted;
    public event EventHandler<BotPositionOpened>? PositionOpened;
    public event EventHandler<TradeCompleted>? TradeCompleted;

    public StrategyExecutor(IContractService contractService, IStrategyEngine engine)
    {
        _contractService = contractService;
        _engine = engine;
        _engine.SignalGenerated += OnSignalGenerated;
        _contractService.OpenContractUpdated += OnOpenContractUpdated;
        _contractService.ProposalUpdated += OnBotProposalUpdated;
    }

    public void Start(StrategyConfig config, string symbol)
    {
        _config = config;
        _symbol = symbol;
        _active = true;
        _recoverStrategy = RecoverStrategyFactory.Create(config);
        _proposalsReady = false;
        _callProposalId = string.Empty;
        _putProposalId = string.Empty;
        AppLogger.Info(Src, $"Executor started for {symbol}, TP={config.TakeProfitUsd}, SL={config.StopLossUsd}, trailing={config.EnableTrailingStop}, recover={config.RecoverMode}");
        _ = SubscribeBotProposalsAsync();
    }

    public void Stop()
    {
        _active = false;
        _proposalsReady = false;
        _ = UnsubscribeBotProposalsAsync();
        AppLogger.Info(Src, "Executor stopped");
    }

    public void UpdateCurrentSpot(decimal spot)
    {
        _currentSpot = spot;
    }

    public void ResolveExpiredPositionsLocally(decimal lastCandleClose)
    {
        List<TrackedPosition>? expired = null;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        lock (_positions)
        {
            foreach (var kvp in _positions)
            {
                if (kvp.Value.ExpiryEpoch <= now && !kvp.Value.IsSelling && !kvp.Value.IsResolvedLocally)
                {
                    expired ??= new List<TrackedPosition>();
                    expired.Add(kvp.Value);
                }
            }
        }

        if (expired == null) return;

        AppLogger.Info(Src, $"Resolving {expired.Count} position(s) locally via candle close={lastCandleClose}");

        foreach (var pos in expired)
        {
            bool won;
            if (pos.Direction == SignalDirection.Call)
                won = lastCandleClose > pos.EntrySpot;
            else
                won = lastCandleClose < pos.EntrySpot;

            decimal estimatedProfit = won ? pos.BuyPrice * 0.5m : -pos.BuyPrice;

            pos.IsResolvedLocally = true;
            _engine.RecordTradeResult(pos.Signal.ContributingIndicators, won);
            RecordResult(pos, estimatedProfit);
            TradeCompleted?.Invoke(this, new TradeCompleted(pos.ContractId, estimatedProfit, won));
            AppLogger.Info(Src, $"Resolved locally {pos.ContractId}: entry={pos.EntrySpot}, close={lastCandleClose}, won={won} (stake={GetCurrentStake()})");
        }
    }

    public void RefreshProposals()
    {
        if (!_active) return;
        _proposalsReady = false;
        AppLogger.Info(Src, "RefreshProposals — resubscribing bot proposals");
        _ = SubscribeBotProposalsAsync();
    }

    private async Task SubscribeBotProposalsAsync()
    {
        _proposalCts.Cancel();
        _proposalCts.Dispose();
        _proposalCts = new CancellationTokenSource();
        var ct = _proposalCts.Token;

        await _proposalLock.WaitAsync();
        try
        {
            _proposalsReady = false;
            if (ct.IsCancellationRequested) return;

            var stake = GetCurrentStake();
            _proposalStake = stake;
            var duration = _config.DurationApiValue;
            var durationUnit = _config.DurationApiUnit;
            var barrier = _config.Barrier;
            var callType = _config.CallContractType;
            var putType = _config.PutContractType;
            var barrierParam = callType is "CALL" or "CALLE" ? null : barrier;

            await UnsubscribeBotProposalsAsync();

            var callTask = _contractService.SubscribeProposalAsync(
                _symbol, callType, stake, duration, durationUnit,
                barrier: barrierParam, subscriptionKey: BotCallKey);

            var putTask = _contractService.SubscribeProposalAsync(
                _symbol, putType, stake, duration, durationUnit,
                barrier: barrierParam, subscriptionKey: BotPutKey);

            var callResp = await callTask;
            var putResp = await putTask;

            if (ct.IsCancellationRequested) return;

            _callProposalId = callResp.ProposalId;
            _callSubscriptionId = callResp.SubscriptionId;
            _callAskPrice = callResp.AskPrice;
            _putProposalId = putResp.ProposalId;
            _putSubscriptionId = putResp.SubscriptionId;
            _putAskPrice = putResp.AskPrice;
            _proposalsReady = true;

            AppLogger.Info(Src, $"Bot proposals ready: CALL={_callProposalId} ask={_callAskPrice}, PUT={_putProposalId} ask={_putAskPrice}");
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
                AppLogger.Warn(Src, $"Bot proposal subscribe failed: {ex.Message}");
            _proposalsReady = false;
        }
        finally
        {
            _proposalLock.Release();
        }
    }

    private async Task UnsubscribeBotProposalsAsync()
    {
        try
        {
            await _contractService.UnsubscribeProposalAsync(BotCallKey);
            await _contractService.UnsubscribeProposalAsync(BotPutKey);
        }
        catch (Exception ex)
        {
            AppLogger.Warn(Src, $"Bot proposal unsubscribe error: {ex.Message}");
        }
    }

    private async Task ResubscribeAfterBuyAsync(SignalDirection usedDirection)
    {
        var ct = _proposalCts.Token;
        await _proposalLock.WaitAsync();
        try
        {
            if (ct.IsCancellationRequested) return;

            var stake = GetCurrentStake();
            var duration = _config.DurationApiValue;
            var durationUnit = _config.DurationApiUnit;
            var barrier = _config.Barrier;
            var callType = _config.CallContractType;
            var putType = _config.PutContractType;
            var barrierParam = callType is "CALL" or "CALLE" ? null : barrier;

            if (usedDirection == SignalDirection.Call)
            {
                var resp = await _contractService.SubscribeProposalAsync(
                    _symbol, callType, stake, duration, durationUnit,
                    barrier: barrierParam, subscriptionKey: BotCallKey);
                if (ct.IsCancellationRequested) return;
                _callProposalId = resp.ProposalId;
                _callSubscriptionId = resp.SubscriptionId;
                _callAskPrice = resp.AskPrice;
            }
            else
            {
                var resp = await _contractService.SubscribeProposalAsync(
                    _symbol, putType, stake, duration, durationUnit,
                    barrier: barrierParam, subscriptionKey: BotPutKey);
                if (ct.IsCancellationRequested) return;
                _putProposalId = resp.ProposalId;
                _putSubscriptionId = resp.SubscriptionId;
                _putAskPrice = resp.AskPrice;
            }
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
                AppLogger.Warn(Src, $"Re-subscribe after buy failed: {ex.Message}");
        }
        finally
        {
            _proposalLock.Release();
        }
    }

    private void OnBotProposalUpdated(object? sender, ProposalResponse proposal)
    {
        if (!_active || string.IsNullOrEmpty(proposal.SubscriptionId)) return;

        if (proposal.SubscriptionId == _callSubscriptionId && !string.IsNullOrEmpty(proposal.ProposalId))
        {
            _callProposalId = proposal.ProposalId;
            _callAskPrice = proposal.AskPrice;
        }
        else if (proposal.SubscriptionId == _putSubscriptionId && !string.IsNullOrEmpty(proposal.ProposalId))
        {
            _putProposalId = proposal.ProposalId;
            _putAskPrice = proposal.AskPrice;
        }
    }

    private async void OnSignalGenerated(object? sender, TradeSignal signal)
    {
        if (!_active) return;

        if (!await _signalLock.WaitAsync(0))
        {
            AppLogger.Info(Src, "Signal ignored — another signal is being processed");
            return;
        }

        try
        {
            var activeCount = CountActivePositions();
            if (activeCount >= _config.MaxConcurrentContracts)
            {
                AppLogger.Info(Src, $"Signal ignored — max contracts reached ({activeCount}/{_config.MaxConcurrentContracts})");
                return;
            }

            string contractType = signal.Direction == SignalDirection.Call
                ? _config.CallContractType
                : _config.PutContractType;

            var stake = GetCurrentStake();
            BuyResponse result;

            if (_proposalsReady && _proposalStake == stake)
            {
                var proposalId = signal.Direction == SignalDirection.Call
                    ? _callProposalId
                    : _putProposalId;
                var askPrice = signal.Direction == SignalDirection.Call
                    ? _callAskPrice
                    : _putAskPrice;

                AppLogger.Info(Src, $"Buying via pre-subscribed proposal {proposalId}, ask={askPrice}, stake={stake}");
                result = await _contractService.BuyContractAsync(proposalId, askPrice);

                _ = ResubscribeAfterBuyAsync(signal.Direction);
            }
            else
            {
                var barrierForBuy = _config.CallContractType is "CALL" or "CALLE" ? null : _config.Barrier;
                AppLogger.Info(Src, $"Proposals stake mismatch or not ready (proposal={_proposalStake}, current={stake}) — using BuyDirectAsync");
                result = await _contractService.BuyDirectAsync(
                    _symbol,
                    contractType,
                    stake,
                    _config.DurationApiValue,
                    _config.DurationApiUnit,
                    barrierForBuy);

                _ = SubscribeBotProposalsAsync();
            }

            if (result.ContractId > 0)
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var tracked = new TrackedPosition
                {
                    ContractId = result.ContractId,
                    Direction = signal.Direction,
                    BuyPrice = result.BuyPrice,
                    Signal = signal,
                    DynamicStopLoss = -GetEffectiveStopLoss(),
                    EntryEpoch = now,
                    ExpiryEpoch = now + _config.DurationSeconds,
                    EntrySpot = _currentSpot
                };
                lock (_positions)
                    _positions[result.ContractId] = tracked;

                await _contractService.SubscribeOpenContractAsync(result.ContractId);

                var dirLabel = signal.Direction == SignalDirection.Call ? "CALL" : "PUT";
                TradeExecuted?.Invoke(this, $"Comprou {dirLabel} — {signal.Reason}");
                AppLogger.Info(Src, $"Bought {dirLabel} contract {result.ContractId}, stake={stake}, entrySpot={_currentSpot}");
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
            _signalLock.Release();
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
            if (tracked.IsResolvedLocally)
            {
                RemovePosition(update.ContractId);
                AppLogger.Info(Src, $"Contract {update.ContractId} settled by API (already resolved locally, skipping RecordResult)");
                return;
            }
            bool won = update.Profit >= 0;
            _engine.RecordTradeResult(tracked.Signal.ContributingIndicators, won);
            RecordResult(tracked, update.Profit);
            RemovePosition(update.ContractId);
            TradeCompleted?.Invoke(this, new TradeCompleted(update.ContractId, update.Profit, won));
            return;
        }

        if (tracked.IsResolvedLocally) return;

        // Trailing stop logic
        if (_config.EnableTrailingStop && update.Profit > 0)
        {
            decimal tp = GetEffectiveTakeProfit();

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
        var effectiveTp = GetEffectiveTakeProfit();
        if (update.Profit >= effectiveTp)
        {
            if (!update.IsValidToSell) return;
            tracked.IsSelling = true;
            AppLogger.Info(Src, $"TP hit for {update.ContractId}: profit={update.Profit:F2} >= {effectiveTp:F2}");
            await SellPositionAsync(update.ContractId, tracked, update.Profit);
            return;
        }

        // Time-based dynamic stop loss
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var totalDuration = tracked.ExpiryEpoch - tracked.EntryEpoch;
        var timeRemaining = Math.Max(tracked.ExpiryEpoch - now, 1);
        var timeRatio = totalDuration > 0 ? (decimal)timeRemaining / totalDuration : 0m;
        var timeSl = -(GetEffectiveStopLoss() * timeRatio);

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
                TradeCompleted?.Invoke(this, new TradeCompleted(contractId, profit, won));
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
        var previousStake = GetCurrentStake();

        if (profit >= 0)
            Stats.RecordWin(profit);
        else
            Stats.RecordLoss(profit);

        _recoverStrategy?.RecordResult(profit, tracked.BuyPrice);

        StatsUpdated?.Invoke(this, EventArgs.Empty);

        if (_active && GetCurrentStake() != previousStake)
            _ = ResubscribeAndReEvaluateAsync();
    }

    private async Task ResubscribeAndReEvaluateAsync()
    {
        await SubscribeBotProposalsAsync();
        if (_active && _proposalsReady)
        {
            _engine.ResetCooldown();
            _engine.ReEvaluate();
        }
    }

    private decimal GetCurrentStake()
    {
        if (_recoverStrategy == null)
            return _config.Stake;

        var context = new RecoverContext(_config.Stake, _config.TakeProfitUsd, _config.StopLossUsd, _config.Stake);
        return _recoverStrategy.GetNextStake(context);
    }

    private decimal GetEffectiveTakeProfit()
    {
        if (_recoverStrategy == null)
            return _config.TakeProfitUsd;

        var stake = GetCurrentStake();
        var context = new RecoverContext(_config.Stake, _config.TakeProfitUsd, _config.StopLossUsd, stake);
        return _recoverStrategy.GetDynamicTakeProfit(context);
    }

    private decimal GetEffectiveStopLoss()
    {
        if (_recoverStrategy == null)
            return _config.StopLossUsd;

        var stake = GetCurrentStake();
        var context = new RecoverContext(_config.Stake, _config.TakeProfitUsd, _config.StopLossUsd, stake);
        return _recoverStrategy.GetDynamicStopLoss(context);
    }

    private void RemovePosition(long contractId)
    {
        lock (_positions)
            _positions.Remove(contractId);
    }

    private async Task ResolveExpiredPositionsBeforeBuyAsync()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        List<TrackedPosition>? expired = null;
        lock (_positions)
        {
            foreach (var kvp in _positions)
            {
                if (kvp.Value.ExpiryEpoch <= now && !kvp.Value.IsSelling)
                {
                    expired ??= new List<TrackedPosition>();
                    expired.Add(kvp.Value);
                }
            }
            if (expired != null)
            {
                foreach (var pos in expired)
                    _positions.Remove(pos.ContractId);
            }
        }

        if (expired == null) return;

        AppLogger.Info(Src, $"Resolving {expired.Count} expired position(s) before new buy");
        foreach (var pos in expired)
            await ResolveStalePositionAsync(pos);
    }

    private async Task ResolveStaleExpiredPositionsAsync()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        List<TrackedPosition>? stale = null;
        lock (_positions)
        {
            foreach (var kvp in _positions)
            {
                if (kvp.Value.ExpiryEpoch + 3 <= now && !kvp.Value.IsSelling)
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

        if (stale == null) return;

        foreach (var pos in stale)
            await ResolveStalePositionAsync(pos);
    }

    private async Task ResolveStalePositionAsync(TrackedPosition pos)
    {
        decimal profit = -pos.BuyPrice;
        bool won = false;

        try
        {
            var status = await _contractService.GetContractStatusAsync(pos.ContractId);
            if (status != null && (status.IsExpired || status.IsSold || status.Status is "won" or "lost" or "sold"))
            {
                profit = status.Profit;
                won = profit >= 0;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn(Src, $"Failed to query stale contract {pos.ContractId}: {ex.Message}");
        }

        _engine.RecordTradeResult(pos.Signal.ContributingIndicators, won);
        RecordResult(pos, profit);
        TradeCompleted?.Invoke(this, new TradeCompleted(pos.ContractId, profit, won));
        AppLogger.Info(Src, $"Resolved stale position {pos.ContractId}: profit={profit:F2}, won={won} (stake={GetCurrentStake()})");
    }

    private int CountActivePositions()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var grace = _config.StrategyMode == "Tendência" ? 5 : 0;
        lock (_positions)
        {
            int count = 0;
            foreach (var kvp in _positions)
            {
                if (kvp.Value.ExpiryEpoch > now + grace)
                    count++;
            }
            return count;
        }
    }

    public void Dispose()
    {
        _engine.SignalGenerated -= OnSignalGenerated;
        _contractService.OpenContractUpdated -= OnOpenContractUpdated;
        _contractService.ProposalUpdated -= OnBotProposalUpdated;
        _proposalCts.Cancel();
        _proposalCts.Dispose();
        _proposalLock.Dispose();
        _signalLock.Dispose();
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
        public decimal EntrySpot { get; set; }
        public bool IsResolvedLocally { get; set; }
    }
}

public sealed record BotPositionOpened(BuyResponse BuyResult, string ContractType);
public sealed record TradeCompleted(long ContractId, decimal Profit, bool Won);
