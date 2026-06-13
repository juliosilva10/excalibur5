using Excalibur5.Models;
using Excalibur5.Models.Diversity;
using Excalibur5.Services;

namespace Excalibur5.Services.Strategy.Diversity;

/// <summary>A leg that was successfully bought, with the broker contract id and stake.</summary>
public sealed record OpenLeg(long ContractId, DiversityLeg Leg, decimal BuyPrice);

/// <summary>Raised when every leg of a group has settled and the net result is known.</summary>
public sealed record DiversityGroupCompleted(GroupResult Result);

/// <summary>Raised when a group's legs have all been bought and are now live.</summary>
public sealed record DiversityGroupOpened(Guid GroupId, IReadOnlyList<OpenLeg> Legs);

/// <summary>
/// Executes Diversity groups: buys all legs (possibly across different markets) as a unit, tracks
/// them via the broker's open-contract stream, and consolidates the per-leg settlements through a
/// <see cref="GroupResultAggregator"/> into a single <see cref="DiversityGroupCompleted"/> event.
///
/// Runs parallel to the single-contract <c>StrategyExecutor</c> and reuses the symbol-agnostic
/// parts of <see cref="IContractService"/> (BuyDirect/Sell/SubscribeOpenContract). It does NOT
/// touch the single-contract executor's state.
///
/// Partial-failure policy: if any leg fails to buy, the legs already bought are sold back and the
/// group is aborted (no half-open exposure).
/// </summary>
public sealed class DiversityCoordinator : IDisposable
{
    private const string Src = "DiversityCoordinator";

    private readonly IContractService _contractService;
    private readonly GroupResultAggregator _aggregator = new();

    // Serializes group buys so two groups can't interleave their leg purchases.
    private readonly SemaphoreSlim _buyLock = new(1, 1);

    public event EventHandler<DiversityGroupOpened>? GroupOpened;
    public event EventHandler<DiversityGroupCompleted>? GroupCompleted;
    public event EventHandler<string>? StatusMessage;

    public DiversityCoordinator(IContractService contractService)
    {
        _contractService = contractService;
        _contractService.OpenContractUpdated += OnOpenContractUpdated;
    }

    /// <summary>Number of groups currently live (bought, awaiting settlement).</summary>
    public int PendingGroupCount => _aggregator.PendingGroupCount;

    /// <summary>
    /// Buys every leg of <paramref name="config"/> as a unit. On success registers the group with
    /// the aggregator and raises <see cref="GroupOpened"/>, returning the new group id. On any leg
    /// failure, sells back the legs already bought and returns null (group aborted).
    /// </summary>
    public async Task<Guid?> ExecuteGroupAsync(DiversityGroupConfig config, CancellationToken ct = default)
    {
        if (config.Legs.Count < 1)
        {
            AppLogger.Warn(Src, "ExecuteGroupAsync called with no legs");
            return null;
        }

        await _buyLock.WaitAsync(ct);
        try
        {
            var bought = new List<OpenLeg>();
            foreach (var leg in config.Legs)
            {
                BuyResponse result;
                try
                {
                    result = await _contractService.BuyDirectAsync(
                        leg.Symbol, leg.ContractType, leg.Stake,
                        leg.DurationTicks, "t", leg.Barrier, ct);
                }
                catch (Exception ex)
                {
                    AppLogger.Warn(Src, $"Leg buy threw for {leg.Symbol}/{leg.ContractType}: {ex.Message}");
                    result = new BuyResponse { Error = ex.Message };
                }

                if (!result.Success || result.ContractId <= 0)
                {
                    AppLogger.Warn(Src, $"Leg failed ({leg.Symbol}/{leg.ContractType}): {result.Error}. Aborting group, unwinding {bought.Count} leg(s).");
                    StatusMessage?.Invoke(this, $"Falha numa perna ({leg.ContractType}). Desfazendo o grupo.");
                    await UnwindAsync(bought);
                    return null;
                }

                bought.Add(new OpenLeg(result.ContractId, leg, result.BuyPrice));
            }

            var groupId = Guid.NewGuid();
            var contractIds = bought.ConvertAll(b => b.ContractId);
            _aggregator.RegisterGroup(groupId, contractIds, config.TotalStake);

            // Subscribe after registering so a fast settlement can't race ahead of the aggregator.
            // If any subscribe fails we must NOT leave a registered-but-unsubscribed group: that
            // would keep PendingGroupCount > 0 forever and wedge the signal gate. Roll back instead.
            try
            {
                foreach (var leg in bought)
                    await _contractService.SubscribeOpenContractAsync(leg.ContractId, ct);
            }
            catch (Exception ex)
            {
                AppLogger.Warn(Src, $"Subscribe failed for group {groupId}: {ex.Message}. Rolling back and unwinding.");
                _aggregator.DeregisterGroup(groupId);
                await UnwindAsync(bought);
                StatusMessage?.Invoke(this, "Falha ao assinar contratos do grupo. Grupo desfeito.");
                return null;
            }

            AppLogger.Info(Src, $"Group {groupId} opened with {bought.Count} legs, totalStake={config.TotalStake}");
            GroupOpened?.Invoke(this, new DiversityGroupOpened(groupId, bought));
            return groupId;
        }
        finally
        {
            _buyLock.Release();
        }
    }

    /// <summary>Force-resolves any unsettled legs of a group (e.g. on timeout) with a given profit.</summary>
    public void ForceSettleGroup(Guid groupId, decimal profitForUnsettled)
    {
        var result = _aggregator.ForceSettleRemaining(groupId, profitForUnsettled);
        if (result != null)
            GroupCompleted?.Invoke(this, new DiversityGroupCompleted(result));
    }

    private void OnOpenContractUpdated(object? sender, OpenContractUpdate update)
    {
        if (!_aggregator.IsGroupContract(update.ContractId)) return;

        bool settled = update.IsExpired || update.IsSold
            || update.Status is "sold" or "won" or "lost";
        if (!settled) return;

        var result = _aggregator.RecordLegSettled(update.ContractId, update.Profit);
        if (result != null)
        {
            AppLogger.Info(Src, $"Group {result.GroupId} completed: net={result.TotalProfit:F2}, won={result.Won}");
            GroupCompleted?.Invoke(this, new DiversityGroupCompleted(result));
        }
    }

    private async Task UnwindAsync(IReadOnlyList<OpenLeg> bought)
    {
        foreach (var leg in bought)
        {
            try
            {
                await _contractService.SellContractAsync(leg.ContractId);
            }
            catch (Exception ex)
            {
                AppLogger.Warn(Src, $"Failed to unwind leg {leg.ContractId}: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        _contractService.OpenContractUpdated -= OnOpenContractUpdated;
        _buyLock.Dispose();
    }
}
