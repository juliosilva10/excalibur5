namespace Excalibur5.Services.Strategy;

/// <summary>
/// Pure money-management math for the "Deficit Recovery" mode. Extracted from
/// ContractPanelViewModel so the sizing formula is unit-testable in isolation —
/// no UI state, no I/O. The ViewModel owns the running deficit/payout state and
/// calls these helpers to compute the next stake.
/// </summary>
public static class RecoveryStakeCalculator
{
    /// <summary>Default payout ratio assumed before any real payouts have been observed.</summary>
    public const decimal DefaultPayoutRatio = 0.5m;

    /// <summary>
    /// Average of the observed payout ratios. Returns <see cref="DefaultPayoutRatio"/>
    /// when no samples are available yet.
    /// </summary>
    public static decimal AveragePayoutRatio(IReadOnlyList<decimal> ratios, int count)
    {
        if (count <= 0) return DefaultPayoutRatio;
        var n = Math.Min(count, ratios.Count);
        if (n <= 0) return DefaultPayoutRatio;

        decimal sum = 0;
        for (int i = 0; i < n; i++) sum += ratios[i];
        return sum / n;
    }

    /// <summary>
    /// Computes the next stake needed to recover the running deficit over the configured
    /// number of recovery trades, clamped to [baseStake, maxStake] and rounded to cents.
    /// When the deficit is cleared, returns baseStake.
    /// </summary>
    public static decimal NextStake(
        decimal deficit, decimal avgPayoutRatio, int recoveryTrades,
        decimal baseStake, decimal maxStake)
    {
        if (deficit <= 0) return baseStake;
        if (recoveryTrades <= 0) recoveryTrades = 1;
        if (avgPayoutRatio <= 0) avgPayoutRatio = DefaultPayoutRatio;

        var needed = deficit / (avgPayoutRatio * recoveryTrades);
        var stake = Math.Max(needed, baseStake);
        if (maxStake > 0) stake = Math.Min(stake, maxStake);
        return Math.Round(stake, 2);
    }
}
