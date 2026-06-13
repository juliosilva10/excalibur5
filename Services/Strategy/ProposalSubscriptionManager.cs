using Excalibur5.Models;
using Excalibur5.Models.Strategy;
using Excalibur5.Services;

namespace Excalibur5.Services.Strategy;

/// <summary>
/// Immutable snapshot of both pre-subscribed bot proposals. Published atomically via a single
/// volatile reference so cross-thread readers always observe a consistent (id+ask+payout) view
/// with no torn decimals.
/// </summary>
public sealed record BotProposalState(
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

/// <summary>
/// Parameters for a (re)subscribe, computed by the caller's factory <b>inside</b> the proposal
/// lock so the stake/config snapshot is taken at the right moment.
/// </summary>
public sealed record ProposalRequest(
    string Symbol, string CallType, string PutType, decimal Stake,
    int Duration, string DurationUnit, string? Barrier);

/// <summary>
/// Owns the pre-subscribed bot proposal subscriptions and their shared concurrency machinery:
/// the immutable snapshot (published under a gate), the serialization semaphore, and the
/// cancellation-token swap. Extracted from StrategyExecutor; the locking semantics are preserved
/// exactly — the CTS is swapped under the gate before awaiting the lock, the request factory is
/// evaluated inside the lock, and the snapshot is only published under the gate.
/// </summary>
public sealed class ProposalSubscriptionManager : IDisposable
{
    public const string BotCallKey = "BOT_CALL";
    public const string BotPutKey = "BOT_PUT";

    private readonly IContractService _contractService;
    private readonly string _src;

    private readonly SemaphoreSlim _proposalLock = new(1, 1);
    // Guards the CTS swap and the snapshot publication so cross-thread readers never see a
    // disposed CTS or a torn/partial snapshot.
    private readonly object _gate = new();
    private CancellationTokenSource _cts = new();

    private volatile BotProposalState _proposals = BotProposalState.Empty;
    private decimal _proposalStake;

    public ProposalSubscriptionManager(IContractService contractService, string src)
    {
        _contractService = contractService;
        _src = src;
    }

    /// <summary>The current atomic snapshot. Read once into a local before using its fields.</summary>
    public BotProposalState Snapshot => _proposals;

    /// <summary>The stake the current proposals were subscribed for.</summary>
    public decimal ProposalStake => _proposalStake;

    /// <summary>Clears the published snapshot (marks proposals not-ready).</summary>
    public void Clear()
    {
        lock (_gate) { _proposals = BotProposalState.Empty; }
    }

    /// <summary>
    /// Cancels any in-flight subscribe, then subscribes fresh CALL+PUT proposals.
    /// <paramref name="requestFactory"/> is evaluated inside the lock so it captures the stake
    /// and config at the correct moment.
    /// </summary>
    public async Task SubscribeAsync(Func<ProposalRequest> requestFactory)
    {
        CancellationToken ct;
        lock (_gate)
        {
            _cts.Cancel();
            _cts.Dispose();
            _cts = new CancellationTokenSource();
            ct = _cts.Token;
        }

        await _proposalLock.WaitAsync();
        try
        {
            lock (_gate) { _proposals = BotProposalState.Empty; }
            if (ct.IsCancellationRequested) return;

            var req = requestFactory();
            _proposalStake = req.Stake;

            await UnsubscribeAsync();

            var callTask = _contractService.SubscribeProposalAsync(
                req.Symbol, req.CallType, req.Stake, req.Duration, req.DurationUnit,
                barrier: req.Barrier, subscriptionKey: BotCallKey);

            var putTask = _contractService.SubscribeProposalAsync(
                req.Symbol, req.PutType, req.Stake, req.Duration, req.DurationUnit,
                barrier: req.Barrier, subscriptionKey: BotPutKey);

            var callResp = await callTask;
            var putResp = await putTask;

            if (ct.IsCancellationRequested) return;

            lock (_gate)
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

            AppLogger.Info(_src, $"Bot proposals ready: CALL={callResp.ProposalId} ask={callResp.AskPrice}, PUT={putResp.ProposalId} ask={putResp.AskPrice}");
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
                AppLogger.Warn(_src, $"Bot proposal subscribe failed: {ex.Message}");
            lock (_gate) { _proposals = BotProposalState.Empty; }
        }
        finally
        {
            _proposalLock.Release();
        }
    }

    /// <summary>
    /// Re-subscribes only the side that was just bought, refreshing that half of the snapshot.
    /// </summary>
    public async Task ResubscribeAfterBuyAsync(SignalDirection usedDirection, Func<ProposalRequest> requestFactory)
    {
        CancellationToken ct;
        lock (_gate) { ct = _cts.Token; }

        await _proposalLock.WaitAsync();
        try
        {
            if (ct.IsCancellationRequested) return;

            var req = requestFactory();

            if (usedDirection == SignalDirection.Call)
            {
                var resp = await _contractService.SubscribeProposalAsync(
                    req.Symbol, req.CallType, req.Stake, req.Duration, req.DurationUnit,
                    barrier: req.Barrier, subscriptionKey: BotCallKey);
                if (ct.IsCancellationRequested) return;
                lock (_gate)
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
                    req.Symbol, req.PutType, req.Stake, req.Duration, req.DurationUnit,
                    barrier: req.Barrier, subscriptionKey: BotPutKey);
                if (ct.IsCancellationRequested) return;
                lock (_gate)
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
                AppLogger.Warn(_src, $"Re-subscribe after buy failed: {ex.Message}");
        }
        finally
        {
            _proposalLock.Release();
        }
    }

    /// <summary>Applies a streamed proposal update to the matching side of the snapshot.</summary>
    public void OnProposalUpdated(ProposalResponse proposal)
    {
        if (string.IsNullOrEmpty(proposal.SubscriptionId)) return;

        // Read-modify-write guarded by the gate so a concurrent (re)subscribe publication
        // can't be lost.
        lock (_gate)
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

    /// <summary>Unsubscribes both bot proposal keys (best-effort).</summary>
    public async Task UnsubscribeAsync()
    {
        try
        {
            await _contractService.UnsubscribeProposalAsync(BotCallKey);
            await _contractService.UnsubscribeProposalAsync(BotPutKey);
        }
        catch (Exception ex)
        {
            AppLogger.Warn(_src, $"Bot proposal unsubscribe error: {ex.Message}");
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _cts.Cancel();
            _cts.Dispose();
        }
        _proposalLock.Dispose();
    }
}
