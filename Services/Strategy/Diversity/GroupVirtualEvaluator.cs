namespace Excalibur5.Services.Strategy.Diversity;

/// <summary>
/// Consolidates the simulated per-leg profits of a Diversity group into a single virtual outcome.
/// Per the product decision, a group counts as ONE win/loss for the virtual sequence: the net of
/// all legs decides it. Keeps the virtual entry-mode controller seeing the group as one contract,
/// consistent with how recover treats it.
/// </summary>
public static class GroupVirtualEvaluator
{
    /// <summary>Net simulated profit across all legs.</summary>
    public static decimal NetProfit(IReadOnlyList<decimal> legProfits)
    {
        decimal sum = 0m;
        foreach (var p in legProfits) sum += p;
        return sum;
    }

    /// <summary>True when the group's net simulated profit is positive (a single 'W').</summary>
    public static bool GroupWon(IReadOnlyList<decimal> legProfits) => NetProfit(legProfits) > 0m;
}
