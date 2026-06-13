namespace Excalibur5.Models.Diversity;

/// <summary>The side of a digit Over/Under prediction.</summary>
public enum DigitSide
{
    Over,
    Under
}

/// <summary>A single digit Over/Under prediction: a side plus a barrier digit (0-9).</summary>
public readonly record struct DigitPrediction(DigitSide Side, int Barrier);

/// <summary>
/// Pure probability helpers for digit Over/Under contracts, used purely to ORIENT the user in the
/// Diversity panel (estimated win chance, dead-digit / coverage warnings, theoretical EV). The
/// live system never prices from these numbers — execution always uses the payout the Deriv
/// proposal returns. Last digits are assumed uniform over 0-9.
/// </summary>
public static class DigitOddsCalculator
{
    /// <summary>The 10 possible last digits.</summary>
    public const int DigitCount = 10;

    /// <summary>Valid barrier range for DIGITOVER is 0-8 (over 9 can never win).</summary>
    public static bool IsValidOverBarrier(int barrier) => barrier is >= 0 and <= 8;

    /// <summary>Valid barrier range for DIGITUNDER is 1-9 (under 0 can never win).</summary>
    public static bool IsValidUnderBarrier(int barrier) => barrier is >= 1 and <= 9;

    public static bool IsValidBarrier(DigitPrediction p) => p.Side switch
    {
        DigitSide.Over => IsValidOverBarrier(p.Barrier),
        DigitSide.Under => IsValidUnderBarrier(p.Barrier),
        _ => false
    };

    /// <summary>True if the given last digit wins this prediction.</summary>
    public static bool Wins(DigitPrediction p, int digit) => p.Side switch
    {
        DigitSide.Over => digit > p.Barrier,
        DigitSide.Under => digit < p.Barrier,
        _ => false
    };

    /// <summary>
    /// Probability of winning, assuming uniform digits. Over n → (9-n)/10; Under n → n/10.
    /// </summary>
    public static double WinProbability(DigitPrediction p)
    {
        int winning = 0;
        for (int d = 0; d < DigitCount; d++)
            if (Wins(p, d)) winning++;
        return (double)winning / DigitCount;
    }

    /// <summary>
    /// Digits for which EVERY leg loses ("dead digits"). With same-tick legs these are the
    /// catastrophic outcomes that lose the whole group stake at once — a coverage gap the user
    /// should be warned about. Only meaningful when all legs share one market/tick.
    /// </summary>
    public static IReadOnlyList<int> DeadDigits(IReadOnlyList<DigitPrediction> legs)
    {
        var dead = new List<int>();
        if (legs.Count == 0) return dead;

        for (int d = 0; d < DigitCount; d++)
        {
            bool anyWins = false;
            foreach (var leg in legs)
                if (Wins(leg, d)) { anyWins = true; break; }
            if (!anyWins) dead.Add(d);
        }
        return dead;
    }

    /// <summary>Digits for which ALL legs win simultaneously (double/multi-win zone).</summary>
    public static IReadOnlyList<int> FullWinDigits(IReadOnlyList<DigitPrediction> legs)
    {
        var full = new List<int>();
        if (legs.Count == 0) return full;

        for (int d = 0; d < DigitCount; d++)
        {
            bool allWin = true;
            foreach (var leg in legs)
                if (!Wins(leg, d)) { allWin = false; break; }
            if (allWin) full.Add(d);
        }
        return full;
    }

    /// <summary>True when no digit loses all legs — every outcome wins at least one leg.</summary>
    public static bool IsFullCoverage(IReadOnlyList<DigitPrediction> legs)
        => DeadDigits(legs).Count == 0;
}
