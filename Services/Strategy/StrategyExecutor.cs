using Excalibur5.Models;
using Excalibur5.Models.Strategy;
using Excalibur5.Services;
using Excalibur5.Services.Strategy.Recovery;
using Excalibur5.Services.Strategy.Virtual;

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
    private readonly IVirtualEntryModeController _entryModeController;
    private readonly IVirtualTradeSimulator _virtualTradeSimulator;
    private readonly Dictionary<long, TrackedPosition> _positions = new();
    private readonly HashSet<long> _recoveredUnconfirmedContracts = new();
    private readonly SemaphoreSlim _proposalLock = new(1, 1);
    private readonly SemaphoreSlim _signalLock = new(1, 1);
    // Guards the _proposalCts swap and the _proposals snapshot publication so that
    // cross-thread readers never see a disposed CTS or a torn/partial snapshot.
    private readonly object _proposalGate = new();
    private CancellationTokenSource _proposalCts = new();
    private StrategyConfig _config = new();
    private string _symbol = string.Empty;
    private volatile bool _active;
    private IRecoverStrategy? _recoverStrategy;
    private decimal _currentSpot;
    private bool _ownsVirtualReservation;

    // Pre-subscribed proposal state — published as one immutable snapshot via a single
    // volatile reference. Reference assignment is atomic, so readers on the WS/signal
    // threads always observe a consistent (id+ask+payout) view with no torn decimals.
    private volatile BotProposalState _proposals = BotProposalState.Empty;
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
    public event EventHandler<VirtualTradeResult>? VirtualTradeCompleted;
    public event EventHandler<VirtualPositionOpened>? VirtualPositionOpened;
    public event EventHandler<VirtualPositionUpdated>? VirtualPositionUpdated;

    public StrategyExecutor(
        IContractService contractService,
        IStrategyEngine engine,
        IVirtualEntryModeController entryModeController,
        IVirtualTradeSimulator virtualTradeSimulator)
    {
        _contractService = contractService;
        _engine = engine;
        _entryModeController = entryModeController;
        _virtualTradeSimulator = virtualTradeSimulator;
        _engine.SignalGenerated += OnSignalGenerated;
        _contractService.OpenContractUpdated += OnOpenContractUpdated;
        _contractService.ProposalUpdated += OnBotProposalUpdated;
        _virtualTradeSimulator.TradeCompleted += OnVirtualTradeCompleted;
        _virtualTradeSimulator.TradeUpdated += OnVirtualTradeUpdated;
    }

    public void Start(StrategyConfig config, string symbol)
    {
        _config = config;
        _symbol = symbol;
        _active = true;
        _virtualTradeSimulator.Cancel();
        _recoverStrategy = RecoverStrategyFactory.Create(config);
        lock (_proposalGate) { _proposals = BotProposalState.Empty; }
        AppLogger.Info(Src, $"Executor started for {symbol}, TP={config.TakeProfitUsd}, SL={config.StopLossUsd}, trailing={config.EnableTrailingStop}, recover={config.RecoverMode}");
        _ = SubscribeBotProposalsAsync().ContinueWith(
            t => AppLogger.Warn(Src, $"Initial proposal subscribe error: {t.Exception?.InnerException?.Message}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    public void Pause()
    {
        DeactivateExecution();
        _virtualTradeSimulator.Pause();
        AppLogger.Info(Src, "Executor paused");
    }

    public void Resume(StrategyConfig config, string symbol)
    {
        _config = config;
        _symbol = symbol;
        _active = true;
        _virtualTradeSimulator.Resume();
        _ = SubscribeBotProposalsAsync().ContinueWith(
            task => AppLogger.Warn(Src, $"Proposal resume error: {task.Exception?.InnerException?.Message}"),
            TaskContinuationOptions.OnlyOnFaulted);
        AppLogger.Info(Src, "Executor resumed");
    }

    public void Stop()
    {
        DeactivateExecution();
        _virtualTradeSimulator.Cancel();
        ReleaseVirtualReservation();
        AppLogger.Info(Src, "Executor stopped");
    }

    private void DeactivateExecution()
    {
        _active = false;
        lock (_proposalGate) { _proposals = BotProposalState.Empty; }
        _pendingSignal = null;
        _executingPendingSignal = false;
        _executingEntryCandleIndex = null;
        _executingEntryCandleType = null;
        _ = UnsubscribeBotProposalsAsync().ContinueWith(
            task => AppLogger.Warn(
                Src,
                $"Proposal unsubscribe error: {task.Exception?.InnerException?.Message}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    public void UpdateCurrentSpot(decimal spot)
    {
        _currentSpot = spot;
        _virtualTradeSimulator.UpdateSpot(spot);
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

            // Use the real payout captured at buy time; fall back to a 50% heuristic only
            // if the broker didn't report a payout for this contract.
            decimal estimatedProfit;
            if (won)
                estimatedProfit = pos.Payout > 0 ? pos.Payout - pos.BuyPrice : pos.BuyPrice * 0.5m;
            else
                estimatedProfit = -pos.BuyPrice;

            pos.IsResolvedLocally = true;
            RecordCompletedRealTrade(pos, estimatedProfit, won);
            TradeCompleted?.Invoke(this, new TradeCompleted(pos.ContractId, estimatedProfit, won));
            AppLogger.Info(Src, $"Resolved locally {pos.ContractId}: entry={pos.EntrySpot}, close={lastCandleClose}, won={won} (buyPrice={pos.BuyPrice}, currentStake={GetCurrentStake()})");
        }
    }

    public void RefreshProposals()
    {
        if (!_active) return;
        lock (_proposalGate) { _proposals = BotProposalState.Empty; }
        AppLogger.Info(Src, "RefreshProposals — resubscribing bot proposals");
        _ = SubscribeBotProposalsAsync().ContinueWith(
            t => AppLogger.Warn(Src, $"RefreshProposals error: {t.Exception?.InnerException?.Message}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    private async Task SubscribeBotProposalsAsync()
    {
        CancellationToken ct;
        lock (_proposalGate)
        {
            _proposalCts.Cancel();
            _proposalCts.Dispose();
            _proposalCts = new CancellationTokenSource();
            ct = _proposalCts.Token;
        }

        await _proposalLock.WaitAsync();
        try
        {
            lock (_proposalGate) { _proposals = BotProposalState.Empty; }
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

            lock (_proposalGate)
            {
                _proposals = new BotProposalState(
                    Ready: true,
                    CallProposalId: callResp.ProposalId,
                    CallSubscriptionId: callResp.SubscriptionId,
                    CallAskPrice: callResp.AskPrice,
                    CallPayout: callResp.Payout,
                    PutProposalId: putResp.ProposalId,
                    PutSubscriptionId: putResp.SubscriptionId,
                    PutAskPrice: putResp.AskPrice,
                    PutPayout: putResp.Payout);
            }

            AppLogger.Info(Src, $"Bot proposals ready: CALL={callResp.ProposalId} ask={callResp.AskPrice}, PUT={putResp.ProposalId} ask={putResp.AskPrice}");
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
                AppLogger.Warn(Src, $"Bot proposal subscribe failed: {ex.Message}");
            lock (_proposalGate) { _proposals = BotProposalState.Empty; }
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
        CancellationToken ct;
        lock (_proposalGate) { ct = _proposalCts.Token; }

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
                lock (_proposalGate)
                {
                    _proposals = _proposals with
                    {
                        CallProposalId = resp.ProposalId,
                        CallSubscriptionId = resp.SubscriptionId,
                        CallAskPrice = resp.AskPrice,
                        CallPayout = resp.Payout
                    };
                }
            }
            else
            {
                var resp = await _contractService.SubscribeProposalAsync(
                    _symbol, putType, stake, duration, durationUnit,
                    barrier: barrierParam, subscriptionKey: BotPutKey);
                if (ct.IsCancellationRequested) return;
                lock (_proposalGate)
                {
                    _proposals = _proposals with
                    {
                        PutProposalId = resp.ProposalId,
                        PutSubscriptionId = resp.SubscriptionId,
                        PutAskPrice = resp.AskPrice,
                        PutPayout = resp.Payout
                    };
                }
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

        // Read-modify-write of the snapshot guarded by the gate so a concurrent
        // (re)subscribe publication can't be lost.
        lock (_proposalGate)
        {
            var current = _proposals;
            if (proposal.SubscriptionId == current.CallSubscriptionId && !string.IsNullOrEmpty(proposal.ProposalId))
            {
                _proposals = current with
                {
                    CallProposalId = proposal.ProposalId,
                    CallAskPrice = proposal.AskPrice,
                    CallPayout = proposal.Payout
                };
            }
            else if (proposal.SubscriptionId == current.PutSubscriptionId && !string.IsNullOrEmpty(proposal.ProposalId))
            {
                _proposals = current with
                {
                    PutProposalId = proposal.ProposalId,
                    PutAskPrice = proposal.AskPrice,
                    PutPayout = proposal.Payout
                };
            }
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
        var realCycleId = 0;

        try
        {
            if ((RequiresTimeSynchronizedEntry() || RequiresTickSynchronizedEntry()) && !_executingPendingSignal)
            {
                _pendingSignal = signal;
                _pendingSignalTime = DateTimeOffset.UtcNow;
                AppLogger.Info(Src, $"Signal queued — waiting for next candle birth ({signal.Direction})");
                TradeExecuted?.Invoke(this, "Sinal detectado — aguardando início do próximo candle");
                return;
            }

            if (_entryModeController.IsVirtualMode)
            {
                StartVirtualTrade(signal);
                return;
            }

            await ResolveExpiredPositionsBeforeBuyAsync();
            if (HasReachedPositionLimit()) return;

            contractType = signal.Direction == SignalDirection.Call
                ? _config.CallContractType
                : _config.PutContractType;

            realCycleId = _entryModeController.CurrentRealCycleId;
            stake = GetCurrentStake();
            buyRequestedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            BuyResponse result;

            if (CanUseSubscribedProposal(signal.Direction, stake))
            {
                // Read the snapshot once so id and ask price come from the same atomic view.
                var snapshot = _proposals;
                var proposalId = signal.Direction == SignalDirection.Call
                    ? snapshot.CallProposalId
                    : snapshot.PutProposalId;
                var askPrice = signal.Direction == SignalDirection.Call
                    ? snapshot.CallAskPrice
                    : snapshot.PutAskPrice;

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
                    Payout = result.Payout,
                    Signal = signal,
                    DynamicStopLoss = -GetEffectiveStopLoss(),
                    EntryEpoch = now,
                    ExpiryEpoch = now + _config.DurationSeconds + 5,
                    EntrySpot = _currentSpot,
                    EntryCandleIndex = _executingEntryCandleIndex,
                    EntryCandleType = _executingEntryCandleType,
                    RealCycleId = realCycleId
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
            if (await TryRecoverUnconfirmedBuyAsync(
                    signal,
                    contractType,
                    stake,
                    buyRequestedAt,
                    realCycleId))
                return;

            AppLogger.Warn(Src, $"Buy failed: {ex.Message}");
            TradeExecuted?.Invoke(this, $"Erro: {ex.Message}");
        }
        finally
        {
            _signalLock.Release();
        }
    }

    private void StartVirtualTrade(TradeSignal signal)
    {
        if (!_entryModeController.TryReserveVirtualEntry())
        {
            TradeExecuted?.Invoke(this, "Sinal virtual ignorado: simulação em andamento");
            return;
        }

        _ownsVirtualReservation = true;
        var tradeId = VirtualTradeIdGenerator.Next();
        var durationTicks = _config.DurationApiUnit == "t"
            ? _config.DurationApiValue
            : 0;
        var winProfit = GetVirtualWinProfit(signal.Direction, _config.Stake);
        var request = new VirtualTradeRequest(
            tradeId,
            signal,
            _currentSpot,
            _config.DurationSeconds,
            durationTicks,
            winProfit);

        if (!_virtualTradeSimulator.TryStart(request))
        {
            ReleaseVirtualReservation();
            AppLogger.Info(Src, "Virtual signal ignored — another simulation is active");
            TradeExecuted?.Invoke(this, "Sinal virtual ignorado: simulação em andamento");
            return;
        }

        var direction = signal.Direction == SignalDirection.Call ? "CALL" : "PUT";
        var contractType = signal.Direction == SignalDirection.Call
            ? _config.CallContractType
            : _config.PutContractType;
        VirtualPositionOpened?.Invoke(this, new VirtualPositionOpened(
            tradeId,
            _symbol,
            contractType,
            _config.Stake,
            _currentSpot,
            _config.DurationSeconds,
            winProfit));
        AppLogger.Info(Src, $"Virtual {direction} started at {_currentSpot}");
        TradeExecuted?.Invoke(this, $"Entrada virtual {direction} iniciada");
    }

    private decimal GetVirtualWinProfit(SignalDirection direction, decimal stake)
    {
        var snapshot = _proposals;
        var payout = direction == SignalDirection.Call ? snapshot.CallPayout : snapshot.PutPayout;
        if (snapshot.Ready && payout > 0 && stake > 0)
            return payout - stake;

        return 0m;
    }

    private void OnVirtualTradeCompleted(object? sender, Virtual.VirtualTradeCompleted result)
    {
        if (!_active) return;

        _engine.RecordTradeResult(result.Signal.ContributingIndicators, result.Won);
        var realModeActivated = _entryModeController.RecordVirtualResult(result.Won);
        _ownsVirtualReservation = false;
        VirtualTradeCompleted?.Invoke(
            this,
            new VirtualTradeResult(result.TradeId, result.Won, realModeActivated, result.ExitSpot, result.WinProfit));

        var outcome = result.Won ? "W" : "L";
        var suffix = realModeActivated ? " — próxima entrada será real" : string.Empty;
        AppLogger.Info(Src, $"Virtual trade completed: {outcome}{suffix}");
    }

    private void OnVirtualTradeUpdated(object? sender, Virtual.VirtualTradeUpdated update)
    {
        VirtualPositionUpdated?.Invoke(
            this,
            new VirtualPositionUpdated(update.TradeId, update.CurrentSpot));
    }

    private void ReleaseVirtualReservation()
    {
        if (!_ownsVirtualReservation) return;

        _entryModeController.CancelVirtualEntry();
        _ownsVirtualReservation = false;
    }

    private bool HasReachedPositionLimit()
    {
        var activeCount = CountActivePositions();
        if (activeCount < _config.MaxConcurrentContracts)
        {
            AppLogger.Info(Src, $"Active positions: {activeCount}/{_config.MaxConcurrentContracts}");
            return false;
        }

        AppLogger.Info(
            Src,
            $"Signal ignored — max contracts reached ({activeCount}/{_config.MaxConcurrentContracts})");
        return true;
    }

    private async Task<bool> TryRecoverUnconfirmedBuyAsync(
        TradeSignal signal,
        string contractType,
        decimal stake,
        long requestedAt,
        int realCycleId)
    {
        if (stake <= 0 || string.IsNullOrWhiteSpace(contractType) || requestedAt <= 0)
            return false;

        AppLogger.Warn(Src, $"Buy response failed after order send; reconciling stake={stake}");
        foreach (var delay in UnconfirmedBuyRecoveryDelays)
        {
            await Task.Delay(delay);
            var entry = await FindUnconfirmedBuyAsync(contractType, stake, requestedAt);
            if (entry == null) continue;

            RecoverSettledBuy(entry, signal, contractType, stake, realCycleId);
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
        decimal fallbackStake,
        int realCycleId)
    {
        var buy = CreateRecoveredBuyResponse(entry, fallbackStake);
        var recovered = CreateRecoveredPosition(entry, signal, buy.BuyPrice, realCycleId);
        bool won = entry.ProfitLoss >= 0;

        PositionOpened?.Invoke(this, new BotPositionOpened(buy, contractType, null, null));
        RecordCompletedRealTrade(recovered, entry.ProfitLoss, won);
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
        decimal stake,
        int realCycleId)
    {
        return new TrackedPosition
        {
            ContractId = entry.ContractId,
            Direction = signal.Direction,
            BuyPrice = stake,
            Signal = signal,
            EntryEpoch = entry.PurchaseTime,
            ExpiryEpoch = entry.SellTime,
            RealCycleId = realCycleId
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
        var snapshot = _proposals;
        if (!snapshot.Ready || !IsSameStake(_proposalStake, stake)) return false;

        var proposalId = direction == SignalDirection.Call ? snapshot.CallProposalId : snapshot.PutProposalId;
        var askPrice = direction == SignalDirection.Call ? snapshot.CallAskPrice : snapshot.PutAskPrice;
        if (!string.IsNullOrWhiteSpace(proposalId) && askPrice > 0) return true;

        AppLogger.Warn(Src, $"Subscribed proposal not ready for {direction}: id='{proposalId}', ask={askPrice}");
        return false;
    }

    private bool RequiresSynchronizedTickEntry()
    {
        return _config.DurationApiUnit == "t"
            || _config.StrategyMode is StrategyModeKeys.TickScalper or StrategyModeKeys.CandleDynamics;
    }

    private bool RequiresTimeSynchronizedEntry()
    {
        return _config.SyncEntryToCandleBoundary
            && _config.DurationApiUnit != "t"
            && _config.StrategyMode is not (StrategyModeKeys.TickScalper or StrategyModeKeys.CandleDynamics);
    }

    private bool RequiresTickSynchronizedEntry()
    {
        return _config.SyncEntryToCandleBoundary
            && (_config.DurationApiUnit == "t"
                || _config.StrategyMode is StrategyModeKeys.TickScalper or StrategyModeKeys.CandleDynamics);
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
            || _config.StrategyMode is StrategyModeKeys.TickScalper or StrategyModeKeys.CandleDynamics;
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
            RecordCompletedRealTrade(tracked, update.Profit, won);
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
                RecordCompletedRealTrade(tracked, profit, won);
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

    private void RecordCompletedRealTrade(TrackedPosition tracked, decimal profit, bool won)
    {
        _engine.RecordTradeResult(tracked.Signal.ContributingIndicators, won);
        var virtualModeActivated = _entryModeController.RecordRealResult(tracked.RealCycleId, won);
        RecordResult(tracked, profit);

        if (virtualModeActivated)
        {
            AppLogger.Info(Src, "Real loss tolerance reached — returning to virtual mode");
            TradeExecuted?.Invoke(this, "Tolerância atingida — retornando às entradas virtuais");
        }
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

        RecordCompletedRealTrade(pos, profit, won);
        TradeCompleted?.Invoke(this, new TradeCompleted(pos.ContractId, profit, won));
        AppLogger.Info(Src, $"Resolved stale position {pos.ContractId}: profit={profit:F2}, won={won} (buyPrice={pos.BuyPrice}, currentStake={GetCurrentStake()})");
        return true;
    }

    private int CountActivePositions()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var grace = _config.StrategyMode == StrategyModeKeys.Trend ? 5 : 0;
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
        ReleaseVirtualReservation();
        _engine.SignalGenerated -= OnSignalGenerated;
        _contractService.OpenContractUpdated -= OnOpenContractUpdated;
        _contractService.ProposalUpdated -= OnBotProposalUpdated;
        _virtualTradeSimulator.TradeCompleted -= OnVirtualTradeCompleted;
        _virtualTradeSimulator.TradeUpdated -= OnVirtualTradeUpdated;
        _virtualTradeSimulator.Dispose();
        lock (_proposalGate)
        {
            _proposalCts.Cancel();
            _proposalCts.Dispose();
        }
        _proposalLock.Dispose();
        _signalLock.Dispose();
    }

    // Immutable snapshot of both pre-subscribed proposals. Published atomically via the
    // volatile _proposals reference so cross-thread readers see a consistent view.
    private sealed record BotProposalState(
        bool Ready,
        string CallProposalId,
        string CallSubscriptionId,
        decimal CallAskPrice,
        decimal CallPayout,
        string PutProposalId,
        string PutSubscriptionId,
        decimal PutAskPrice,
        decimal PutPayout)
    {
        public static readonly BotProposalState Empty =
            new(false, "", "", 0m, 0m, "", "", 0m, 0m);
    }

    private sealed class TrackedPosition
    {
        public long ContractId { get; init; }
        public SignalDirection Direction { get; init; }
        public decimal BuyPrice { get; init; }
        public decimal Payout { get; init; }
        public TradeSignal Signal { get; init; } = null!;
        public decimal DynamicStopLoss { get; set; }
        public long EntryEpoch { get; set; }
        public long ExpiryEpoch { get; set; }
        public bool IsSelling { get; set; }
        public decimal EntrySpot { get; set; }
        public int? EntryCandleIndex { get; set; }
        public ChartSnapshotType? EntryCandleType { get; set; }
        public bool IsResolvedLocally { get; set; }
        public int RealCycleId { get; init; }
    }
}

public sealed record BotPositionOpened(
    BuyResponse BuyResult,
    string ContractType,
    int? EntryCandleIndex,
    ChartSnapshotType? EntryCandleType);
public sealed record TradeCompleted(long ContractId, decimal Profit, bool Won, long SellTime = 0);
public sealed record VirtualTradeResult(long TradeId, bool Won, bool RealModeActivated, decimal ExitSpot, decimal WinProfit);
public sealed record VirtualPositionOpened(
    long TradeId,
    string Symbol,
    string ContractType,
    decimal Stake,
    decimal EntrySpot,
    int DurationSeconds,
    decimal WinProfit);
public sealed record VirtualPositionUpdated(long TradeId, decimal CurrentSpot);
