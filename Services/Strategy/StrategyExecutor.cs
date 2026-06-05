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
    private const int UnconfirmedBuyLookbackSeconds = 30;
    private static readonly int[] UnconfirmedBuyRecoveryDelays = [1000, 3000, 7000, 15000, 30000];

    private readonly IContractService _contractService;
    private readonly IStrategyEngine _engine;
    private readonly Dictionary<long, TrackedPosition> _positions = new();
    private readonly HashSet<long> _recoveredUnconfirmedContracts = new();
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

    // Candle-aligned entry state
    private TradeSignal? _pendingSignal;
    private DateTimeOffset _pendingSignalTime;
    private bool _executingPendingSignal;
    private int? _executingEntryCandleIndex;
    private ChartSnapshotType? _executingEntryCandleType;

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
        _ = SubscribeBotProposalsAsync().ContinueWith(
            t => AppLogger.Warn(Src, $"Initial proposal subscribe error: {t.Exception?.InnerException?.Message}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    public void Stop()
    {
        _active = false;
        _proposalsReady = false;
        _pendingSignal = null;
        _executingPendingSignal = false;
        _executingEntryCandleIndex = null;
        _executingEntryCandleType = null;
        _ = UnsubscribeBotProposalsAsync().ContinueWith(
            t => AppLogger.Warn(Src, $"Proposal unsubscribe error: {t.Exception?.InnerException?.Message}"),
            TaskContinuationOptions.OnlyOnFaulted);
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
            AppLogger.Info(Src, $"Resolved locally {pos.ContractId}: entry={pos.EntrySpot}, close={lastCandleClose}, won={won} (buyPrice={pos.BuyPrice}, currentStake={GetCurrentStake()})");
        }
    }

    public void RefreshProposals()
    {
        if (!_active) return;
        _proposalsReady = false;
        AppLogger.Info(Src, "RefreshProposals — resubscribing bot proposals");
        _ = SubscribeBotProposalsAsync().ContinueWith(
            t => AppLogger.Warn(Src, $"RefreshProposals error: {t.Exception?.InnerException?.Message}"),
            TaskContinuationOptions.OnlyOnFaulted);
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

    private void OnSignalGenerated(object? sender, TradeSignal signal)
    {
        _ = HandleSignalAsync(signal);
    }

    private async Task HandleSignalAsync(TradeSignal signal)
    {
        if (!_active) return;

        if (!await _signalLock.WaitAsync(0))
        {
            AppLogger.Info(Src, "Signal ignored — another signal is being processed");
            return;
        }

        string contractType = string.Empty;
        decimal stake = 0m;
        long buyRequestedAt = 0;

        try
        {
            await ResolveExpiredPositionsBeforeBuyAsync();

            var activeCount = CountActivePositions();
            if (activeCount >= _config.MaxConcurrentContracts)
            {
                AppLogger.Info(Src, $"Signal ignored — max contracts reached ({activeCount}/{_config.MaxConcurrentContracts})");
                return;
            }

            AppLogger.Info(Src, $"Active positions: {activeCount}/{_config.MaxConcurrentContracts}");

            if ((RequiresTimeSynchronizedEntry() || RequiresTickSynchronizedEntry()) && !_executingPendingSignal)
            {
                _pendingSignal = signal;
                _pendingSignalTime = DateTimeOffset.UtcNow;
                AppLogger.Info(Src, $"Signal queued — waiting for next candle birth ({signal.Direction})");
                TradeExecuted?.Invoke(this, "Sinal detectado — aguardando início do próximo candle");
                return;
            }

            contractType = signal.Direction == SignalDirection.Call
                ? _config.CallContractType
                : _config.PutContractType;

            stake = GetCurrentStake();
            buyRequestedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            BuyResponse result;

            if (CanUseSubscribedProposal(signal.Direction, stake))
            {
                var proposalId = signal.Direction == SignalDirection.Call
                    ? _callProposalId
                    : _putProposalId;
                var askPrice = signal.Direction == SignalDirection.Call
                    ? _callAskPrice
                    : _putAskPrice;

                AppLogger.Info(Src, $"Buying via pre-subscribed proposal {proposalId}, ask={askPrice}, stake={stake}");
                result = await _contractService.BuyContractAsync(proposalId, askPrice);

                _ = ResubscribeAfterBuyAsync(signal.Direction).ContinueWith(
                    t => AppLogger.Warn(Src, $"ResubscribeAfterBuy error: {t.Exception?.InnerException?.Message}"),
                    TaskContinuationOptions.OnlyOnFaulted);
            }
            else
            {
                if (RequiresSynchronizedTickEntry())
                {
                    AppLogger.Warn(Src, "Signal skipped - synchronized tick entry requires a ready subscribed proposal");
                    TradeExecuted?.Invoke(this, "Sinal ignorado: proposta do candle ainda não estava pronta");
                    _ = SubscribeBotProposalsAsync().ContinueWith(
                        t => AppLogger.Warn(Src, $"SubscribeBotProposals error: {t.Exception?.InnerException?.Message}"),
                        TaskContinuationOptions.OnlyOnFaulted);
                    return;
                }

                var barrierForBuy = _config.CallContractType is "CALL" or "CALLE" ? null : _config.Barrier;
                AppLogger.Info(Src, $"Proposals stake mismatch or not ready (proposal={_proposalStake}, current={stake}) — using BuyDirectAsync");
                result = await _contractService.BuyDirectAsync(
                    _symbol,
                    contractType,
                    stake,
                    _config.DurationApiValue,
                    _config.DurationApiUnit,
                    barrierForBuy);

                _ = SubscribeBotProposalsAsync().ContinueWith(
                    t => AppLogger.Warn(Src, $"SubscribeBotProposals error: {t.Exception?.InnerException?.Message}"),
                    TaskContinuationOptions.OnlyOnFaulted);
            }

            if (result.ContractId > 0)
            {
                var now = result.StartTime > 0
                    ? result.StartTime
                    : DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var tracked = new TrackedPosition
                {
                    ContractId = result.ContractId,
                    Direction = signal.Direction,
                    BuyPrice = result.BuyPrice,
                    Signal = signal,
                    DynamicStopLoss = -GetEffectiveStopLoss(),
                    EntryEpoch = now,
                    ExpiryEpoch = now + _config.DurationSeconds + 5,
                    EntrySpot = _currentSpot,
                    EntryCandleIndex = _executingEntryCandleIndex,
                    EntryCandleType = _executingEntryCandleType
                };
                lock (_positions)
                    _positions[result.ContractId] = tracked;

                await _contractService.SubscribeOpenContractAsync(result.ContractId);

                var dirLabel = signal.Direction == SignalDirection.Call ? "CALL" : "PUT";
                TradeExecuted?.Invoke(this, $"Comprou {dirLabel} — {signal.Reason}");
                AppLogger.Info(Src, $"Bought {dirLabel} contract {result.ContractId}, stake={stake}, entrySpot={_currentSpot}");
                PositionOpened?.Invoke(this,
                    new BotPositionOpened(result, contractType, _executingEntryCandleIndex, _executingEntryCandleType));
            }
            else
            {
                AppLogger.Warn(Src, $"Buy returned no contract ID: {result.Error}");
                TradeExecuted?.Invoke(this, $"Erro: {result.Error}");
            }
        }
        catch (Exception ex)
        {
            if (await TryRecoverUnconfirmedBuyAsync(signal, contractType, stake, buyRequestedAt))
                return;

            AppLogger.Warn(Src, $"Buy failed: {ex.Message}");
            TradeExecuted?.Invoke(this, $"Erro: {ex.Message}");
        }
        finally
        {
            _signalLock.Release();
        }
    }

    private async Task<bool> TryRecoverUnconfirmedBuyAsync(
        TradeSignal signal,
        string contractType,
        decimal stake,
        long requestedAt)
    {
        if (stake <= 0 || string.IsNullOrWhiteSpace(contractType) || requestedAt <= 0)
            return false;

        AppLogger.Warn(Src, $"Buy response failed after order send; reconciling stake={stake}");
        foreach (var delay in UnconfirmedBuyRecoveryDelays)
        {
            await Task.Delay(delay);
            var entry = await FindUnconfirmedBuyAsync(contractType, stake, requestedAt);
            if (entry == null) continue;

            RecoverSettledBuy(entry, signal, contractType, stake);
            return true;
        }

        return false;
    }

    private async Task<ProfitTableEntry?> FindUnconfirmedBuyAsync(
        string contractType,
        decimal stake,
        long requestedAt)
    {
        var entries = await _contractService.GetProfitTableAsync(limit: 20);
        var match = entries
            .Where(entry => IsUnconfirmedBuyMatch(entry, contractType, stake, requestedAt))
            .OrderByDescending(entry => entry.PurchaseTime)
            .FirstOrDefault();

        return match != null && MarkRecoveredUnconfirmedContract(match.ContractId)
            ? match
            : null;
    }

    private bool IsUnconfirmedBuyMatch(
        ProfitTableEntry entry,
        string contractType,
        decimal stake,
        long requestedAt)
    {
        return entry.ContractId > 0
            && entry.PurchaseTime >= requestedAt - UnconfirmedBuyLookbackSeconds
            && IsSameContractType(entry.ContractType, contractType)
            && IsSameStake(entry.BuyPrice, stake);
    }

    private bool MarkRecoveredUnconfirmedContract(long contractId)
    {
        lock (_positions)
        {
            if (_positions.ContainsKey(contractId)) return false;
            return _recoveredUnconfirmedContracts.Add(contractId);
        }
    }

    private void RecoverSettledBuy(
        ProfitTableEntry entry,
        TradeSignal signal,
        string contractType,
        decimal fallbackStake)
    {
        var buy = CreateRecoveredBuyResponse(entry, fallbackStake);
        var recovered = CreateRecoveredPosition(entry, signal, buy.BuyPrice);
        bool won = entry.ProfitLoss >= 0;

        PositionOpened?.Invoke(this, new BotPositionOpened(buy, contractType, null, null));
        _engine.RecordTradeResult(signal.ContributingIndicators, won);
        RecordResult(recovered, entry.ProfitLoss);
        TradeCompleted?.Invoke(this, new TradeCompleted(entry.ContractId, entry.ProfitLoss, won, entry.SellTime));
        AppLogger.Warn(Src, $"Recovered unconfirmed buy {entry.ContractId}: profit={entry.ProfitLoss:F2}");
    }

    private static BuyResponse CreateRecoveredBuyResponse(ProfitTableEntry entry, decimal fallbackStake)
    {
        return new BuyResponse
        {
            ContractId = entry.ContractId,
            BuyPrice = entry.BuyPrice > 0 ? entry.BuyPrice : fallbackStake,
            StartTime = entry.PurchaseTime
        };
    }

    private static TrackedPosition CreateRecoveredPosition(
        ProfitTableEntry entry,
        TradeSignal signal,
        decimal stake)
    {
        return new TrackedPosition
        {
            ContractId = entry.ContractId,
            Direction = signal.Direction,
            BuyPrice = stake,
            Signal = signal,
            EntryEpoch = entry.PurchaseTime,
            ExpiryEpoch = entry.SellTime,
        };
    }

    private static bool IsSameStake(decimal actual, decimal expected)
    {
        return Math.Abs(actual - expected) <= 0.01m;
    }

    private static bool IsSameContractType(string actual, string expected)
    {
        return actual.Equals(expected, StringComparison.OrdinalIgnoreCase)
            || ContractTypeFormatter.ToDisplayLabel(actual)
                .Equals(ContractTypeFormatter.ToDisplayLabel(expected), StringComparison.OrdinalIgnoreCase);
    }

    private bool CanUseSubscribedProposal(SignalDirection direction, decimal stake)
    {
        if (!_proposalsReady || _proposalStake != stake) return false;

        var proposalId = direction == SignalDirection.Call ? _callProposalId : _putProposalId;
        var askPrice = direction == SignalDirection.Call ? _callAskPrice : _putAskPrice;
        if (!string.IsNullOrWhiteSpace(proposalId) && askPrice > 0) return true;

        AppLogger.Warn(Src, $"Subscribed proposal not ready for {direction}: id='{proposalId}', ask={askPrice}");
        return false;
    }

    private bool RequiresSynchronizedTickEntry()
    {
        return _config.DurationApiUnit == "t"
            || _config.StrategyMode is "Tick Scalper" or "Candle Dynamics";
    }

    private bool RequiresTimeSynchronizedEntry()
    {
        return _config.SyncEntryToCandleBoundary
            && _config.DurationApiUnit != "t"
            && _config.StrategyMode is not ("Tick Scalper" or "Candle Dynamics");
    }

    private bool RequiresTickSynchronizedEntry()
    {
        return _config.SyncEntryToCandleBoundary
            && (_config.DurationApiUnit == "t"
                || _config.StrategyMode is "Tick Scalper" or "Candle Dynamics");
    }

    public void OnTimeCandleBirth(int? entryCandleIndex)
    {
        if (_pendingSignal == null) return;

        var elapsed = DateTimeOffset.UtcNow - _pendingSignalTime;
        if (elapsed.TotalSeconds > _config.DurationSeconds * 2)
        {
            AppLogger.Warn(Src, $"Pending signal expired ({elapsed.TotalSeconds:F0}s old) — discarding");
            _pendingSignal = null;
            return;
        }

        var signal = _pendingSignal;
        _pendingSignal = null;
        _executingPendingSignal = true;
        _executingEntryCandleIndex = entryCandleIndex;
        _executingEntryCandleType = ChartSnapshotType.Candles;
        AppLogger.Info(Src, $"Candle birth — executing queued {signal.Direction} signal");
        _ = HandleSignalAsync(signal).ContinueWith(_ => ClearExecutingSignal(), TaskContinuationOptions.ExecuteSynchronously);
    }

    public void OnTickCandleBirth(int? entryCandleIndex)
    {
        if (_pendingSignal == null) return;

        var signal = _pendingSignal;
        _pendingSignal = null;
        _executingPendingSignal = true;
        _executingEntryCandleIndex = entryCandleIndex;
        _executingEntryCandleType = ChartSnapshotType.TickCandles;
        AppLogger.Info(Src, $"Tick candle birth — executing queued {signal.Direction} signal");
        _ = HandleSignalAsync(signal).ContinueWith(_ => ClearExecutingSignal(), TaskContinuationOptions.ExecuteSynchronously);
    }

    private void ClearExecutingSignal()
    {
        _executingPendingSignal = false;
        _executingEntryCandleIndex = null;
        _executingEntryCandleType = null;
    }

    private bool IsExpiryBoundContract()
    {
        return _config.DurationApiUnit == "t"
            || _config.StrategyMode is "Tick Scalper" or "Candle Dynamics";
    }

    private static void SyncTrackedPosition(TrackedPosition tracked, OpenContractUpdate update)
    {
        if (update.DateStart > 0)
            tracked.EntryEpoch = update.DateStart;
        if (update.DateExpiry > 0)
            tracked.ExpiryEpoch = update.DateExpiry;
        if (update.EntrySpot > 0)
            tracked.EntrySpot = update.EntrySpot;
    }

    private void OnOpenContractUpdated(object? sender, OpenContractUpdate update)
    {
        _ = HandleOpenContractUpdatedAsync(update);
    }

    private async Task HandleOpenContractUpdatedAsync(OpenContractUpdate update)
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

        SyncTrackedPosition(tracked, update);

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
            TradeCompleted?.Invoke(this, new TradeCompleted(update.ContractId, update.Profit, won, update.SellTime));
            return;
        }

        if (tracked.IsResolvedLocally) return;
        if (IsExpiryBoundContract()) return;

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
                tracked.IsSelling = false;
                AppLogger.Warn(Src, $"Sell failed for {contractId}: {result.Error}");
            }
        }
        catch (Exception ex)
        {
            tracked.IsSelling = false;
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
            _ = ResubscribeProposalsOnlyAsync();
    }

    private async Task ResubscribeProposalsOnlyAsync()
    {
        await SubscribeBotProposalsAsync();
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

    private async Task<bool> ResolveStalePositionAsync(TrackedPosition pos)
    {
        decimal profit = -pos.BuyPrice;
        bool won = false;
        bool resolved = false;

        try
        {
            var status = await _contractService.GetContractStatusAsync(pos.ContractId);
            if (status != null && (status.IsExpired || status.IsSold || status.Status is "won" or "lost" or "sold"))
            {
                profit = status.Profit;
                won = profit >= 0;
                resolved = true;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn(Src, $"Failed to query stale contract {pos.ContractId}: {ex.Message}");
        }

        if (!resolved)
        {
            lock (_positions)
                _positions[pos.ContractId] = pos;
            AppLogger.Info(Src, $"Position {pos.ContractId} not yet settled by API — re-queued");
            return false;
        }

        _engine.RecordTradeResult(pos.Signal.ContributingIndicators, won);
        RecordResult(pos, profit);
        TradeCompleted?.Invoke(this, new TradeCompleted(pos.ContractId, profit, won));
        AppLogger.Info(Src, $"Resolved stale position {pos.ContractId}: profit={profit:F2}, won={won} (buyPrice={pos.BuyPrice}, currentStake={GetCurrentStake()})");
        return true;
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
        public long EntryEpoch { get; set; }
        public long ExpiryEpoch { get; set; }
        public bool IsSelling { get; set; }
        public decimal EntrySpot { get; set; }
        public int? EntryCandleIndex { get; set; }
        public ChartSnapshotType? EntryCandleType { get; set; }
        public bool IsResolvedLocally { get; set; }
    }
}

public sealed record BotPositionOpened(
    BuyResponse BuyResult,
    string ContractType,
    int? EntryCandleIndex,
    ChartSnapshotType? EntryCandleType);
public sealed record TradeCompleted(long ContractId, decimal Profit, bool Won, long SellTime = 0);
