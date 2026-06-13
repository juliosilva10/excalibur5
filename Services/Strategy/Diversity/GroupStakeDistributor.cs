using Excalibur5.Models.Diversity;

namespace Excalibur5.Services.Strategy.Diversity;

/// <summary>
/// Distributes a target total stake (decided by the recover strategy, which treats the whole group
/// as one contract) across the group's legs, keeping each leg's share proportional to its
/// configured base stake and respecting a per-leg minimum. Pure and testable.
/// </summary>
public static class GroupStakeDistributor
{
    /// <summary>Deriv's typical minimum stake per contract.</summary>
    public const decimal DefaultMinStake = 0.35m;

    /// <summary>
    /// Returns new per-leg stakes that sum to (approximately) <paramref name="targetTotal"/>, split
    /// in proportion to each leg's configured stake. Each leg gets at least <paramref name="minStake"/>.
    /// Rounded to cents; any rounding remainder is added to the largest leg so the sum matches.
    /// </summary>
    public static IReadOnlyList<decimal> Distribute(
        IReadOnlyList<DiversityLeg> legs, decimal targetTotal, decimal minStake = DefaultMinStake)
    {
        if (legs.Count == 0) return Array.Empty<decimal>();

        decimal baseSum = 0m;
        foreach (var l in legs) baseSum += l.Stake;

        var result = new decimal[legs.Count];

        // Degenerate: no configured weights → split evenly.
        if (baseSum <= 0m)
        {
            var even = Math.Round(targetTotal / legs.Count, 2);
            for (int i = 0; i < legs.Count; i++) result[i] = Math.Max(even, minStake);
            return result;
        }

        decimal assigned = 0m;
        int largestIdx = 0;
        for (int i = 0; i < legs.Count; i++)
        {
            var share = Math.Round(targetTotal * (legs[i].Stake / baseSum), 2);
            if (share < minStake) share = minStake;
            result[i] = share;
            assigned += share;
            if (legs[i].Stake > legs[largestIdx].Stake) largestIdx = i;
        }

        // Reconcile rounding drift onto the largest leg, without dropping below the minimum.
        var drift = Math.Round(targetTotal - assigned, 2);
        if (drift != 0m)
        {
            var adjusted = result[largestIdx] + drift;
            if (adjusted >= minStake) result[largestIdx] = adjusted;
        }

        return result;
    }

    /// <summary>Builds a new leg list with redistributed stakes, preserving all other leg fields.</summary>
    public static IReadOnlyList<DiversityLeg> ApplyStakes(
        IReadOnlyList<DiversityLeg> legs, IReadOnlyList<decimal> stakes)
    {
        var outLegs = new DiversityLeg[legs.Count];
        for (int i = 0; i < legs.Count; i++)
            outLegs[i] = legs[i] with { Stake = stakes[i] };
        return outLegs;
    }
}
