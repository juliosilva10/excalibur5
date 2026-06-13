using Excalibur5.Models.Diversity;

namespace Excalibur5.Services.Strategy.Diversity;

/// <summary>
/// Collects per-leg settlements for Diversity groups and emits a single consolidated
/// <see cref="GroupResult"/> once every leg of a group has settled. This is what lets recover and
/// virtual treat a multi-leg group as one contract: the group's profit is the net sum of all legs,
/// and <c>Won</c> is whether that net is positive.
///
/// Thread-safe: settlements arrive on WebSocket threads and may land out of order. A leg that never
/// settles is handled by <see cref="ForceSettleRemaining"/> (called on a timeout / pre-buy sweep),
/// mirroring how single-contract stale positions are resolved.
/// </summary>
public sealed class GroupResultAggregator
{
    private sealed class PendingGroup
    {
        public required decimal TotalStake { get; init; }
        public required HashSet<long> ExpectedContracts { get; init; }
        public readonly Dictionary<long, decimal> Settled = new();
    }

    private readonly object _gate = new();
    private readonly Dictionary<Guid, PendingGroup> _groups = new();
    private readonly Dictionary<long, Guid> _contractToGroup = new();

    /// <summary>Number of groups still awaiting at least one leg settlement.</summary>
    public int PendingGroupCount
    {
        get { lock (_gate) return _groups.Count; }
    }

    /// <summary>
    /// Registers a group and the contract IDs of its legs. Must be called once all legs are bought
    /// (so the full set of expected contract IDs is known). Stake is the total committed.
    /// </summary>
    public void RegisterGroup(Guid groupId, IReadOnlyList<long> contractIds, decimal totalStake)
    {
        if (contractIds.Count == 0)
            throw new ArgumentException("A group must have at least one leg.", nameof(contractIds));

        lock (_gate)
        {
            var pending = new PendingGroup
            {
                TotalStake = totalStake,
                ExpectedContracts = new HashSet<long>(contractIds)
            };
            _groups[groupId] = pending;
            foreach (var id in contractIds)
                _contractToGroup[id] = groupId;
        }
    }

    /// <summary>True if the contract belongs to a Diversity group still being aggregated.</summary>
    public bool IsGroupContract(long contractId)
    {
        lock (_gate) return _contractToGroup.ContainsKey(contractId);
    }

    /// <summary>
    /// Records one leg's settled profit. Returns the consolidated <see cref="GroupResult"/> when
    /// this was the last outstanding leg of its group; otherwise null. Idempotent per contract id —
    /// a duplicate settlement for an already-recorded leg is ignored.
    /// </summary>
    public GroupResult? RecordLegSettled(long contractId, decimal profit)
    {
        lock (_gate)
        {
            if (!_contractToGroup.TryGetValue(contractId, out var groupId))
                return null;
            if (!_groups.TryGetValue(groupId, out var group))
                return null;

            if (!group.ExpectedContracts.Contains(contractId))
                return null;
            if (!group.Settled.TryAdd(contractId, profit))
                return null; // already settled this leg

            if (group.Settled.Count < group.ExpectedContracts.Count)
                return null; // still waiting on other legs

            return Finalize(groupId, group);
        }
    }

    /// <summary>
    /// Force-settles any unsettled legs of a group with a supplied profit (e.g. local resolution on
    /// expiry/timeout), then emits the consolidated result. Returns null if the group is unknown or
    /// already finalized. Caller holds no lock.
    /// </summary>
    public GroupResult? ForceSettleRemaining(Guid groupId, decimal profitForUnsettled)
    {
        lock (_gate)
        {
            if (!_groups.TryGetValue(groupId, out var group))
                return null;

            foreach (var id in group.ExpectedContracts)
                group.Settled.TryAdd(id, profitForUnsettled);

            return Finalize(groupId, group);
        }
    }

    // Must be called under _gate.
    private GroupResult Finalize(Guid groupId, PendingGroup group)
    {
        decimal totalProfit = 0m;
        foreach (var kv in group.Settled) totalProfit += kv.Value;

        var contractIds = new List<long>(group.ExpectedContracts);

        // Clean up so the group/contract ids don't leak.
        _groups.Remove(groupId);
        foreach (var id in group.ExpectedContracts)
            _contractToGroup.Remove(id);

        return new GroupResult(
            GroupId: groupId,
            TotalProfit: totalProfit,
            TotalStake: group.TotalStake,
            Won: totalProfit > 0m,
            ContractIds: contractIds);
    }

    /// <summary>
    /// Removes a group without emitting a result (e.g. when registration must be rolled back after
    /// a failed subscribe). Returns the contract ids that were registered. Idempotent.
    /// </summary>
    public IReadOnlyList<long> DeregisterGroup(Guid groupId)
    {
        lock (_gate)
        {
            if (!_groups.TryGetValue(groupId, out var group))
                return Array.Empty<long>();
            var ids = new List<long>(group.ExpectedContracts);
            _groups.Remove(groupId);
            foreach (var id in group.ExpectedContracts)
                _contractToGroup.Remove(id);
            return ids;
        }
    }
}
