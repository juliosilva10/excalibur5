using Excalibur5.Models.Diversity;

namespace Excalibur5.Tests;

internal static class DigitOddsTests
{
    public static Task WinProbabilityMatchesDigitRule()
    {
        // Over n wins on digits > n → (9-n)/10
        TestAssert.True(Math.Abs(DigitOddsCalculator.WinProbability(new(DigitSide.Over, 5)) - 0.4) < 1e-9,
            "Over 5 should win 40% (digits 6-9)");
        // Under n wins on digits < n → n/10
        TestAssert.True(Math.Abs(DigitOddsCalculator.WinProbability(new(DigitSide.Under, 5)) - 0.5) < 1e-9,
            "Under 5 should win 50% (digits 0-4)");
        TestAssert.True(Math.Abs(DigitOddsCalculator.WinProbability(new(DigitSide.Over, 2)) - 0.7) < 1e-9,
            "Over 2 should win 70% (digits 3-9)");
        return Task.CompletedTask;
    }

    public static Task BarrierRangesEnforced()
    {
        TestAssert.True(DigitOddsCalculator.IsValidOverBarrier(8), "Over 8 valid");
        TestAssert.False(DigitOddsCalculator.IsValidOverBarrier(9), "Over 9 can never win → invalid");
        TestAssert.True(DigitOddsCalculator.IsValidUnderBarrier(1), "Under 1 valid");
        TestAssert.False(DigitOddsCalculator.IsValidUnderBarrier(0), "Under 0 can never win → invalid");
        return Task.CompletedTask;
    }

    // Over 5 + Under 5: digit 5 loses both — a dead digit (catastrophic outcome).
    public static Task OverUnderSameBarrierHasDeadDigit()
    {
        var legs = new[]
        {
            new DigitPrediction(DigitSide.Over, 5),
            new DigitPrediction(DigitSide.Under, 5)
        };
        var dead = DigitOddsCalculator.DeadDigits(legs);
        TestAssert.Equal(1, dead.Count, "Over5/Under5 should have exactly one dead digit");
        TestAssert.Equal(5, dead[0], "The dead digit should be 5");
        TestAssert.False(DigitOddsCalculator.IsFullCoverage(legs), "Not full coverage");
        return Task.CompletedTask;
    }

    // Over 2 + Under 7: digits 3,4,5,6 win both; no digit loses both — full coverage.
    public static Task OverlappingBarriersAreFullCoverage()
    {
        var legs = new[]
        {
            new DigitPrediction(DigitSide.Over, 2),
            new DigitPrediction(DigitSide.Under, 7)
        };
        TestAssert.True(DigitOddsCalculator.IsFullCoverage(legs), "Over2/Under7 should be full coverage");
        TestAssert.Equal(0, DigitOddsCalculator.DeadDigits(legs).Count, "No dead digits");

        var full = DigitOddsCalculator.FullWinDigits(legs);
        TestAssert.Equal(4, full.Count, "Digits 3,4,5,6 win both legs");
        TestAssert.True(full.Contains(3) && full.Contains(6), "Full-win zone is 3..6");
        return Task.CompletedTask;
    }

    public static Task WinsRespectsSide()
    {
        TestAssert.True(DigitOddsCalculator.Wins(new(DigitSide.Over, 5), 6), "6 > 5 wins Over 5");
        TestAssert.False(DigitOddsCalculator.Wins(new(DigitSide.Over, 5), 5), "5 is not > 5");
        TestAssert.True(DigitOddsCalculator.Wins(new(DigitSide.Under, 5), 4), "4 < 5 wins Under 5");
        TestAssert.False(DigitOddsCalculator.Wins(new(DigitSide.Under, 5), 5), "5 is not < 5");
        return Task.CompletedTask;
    }
}
