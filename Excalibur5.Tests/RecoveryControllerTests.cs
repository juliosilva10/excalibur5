using Excalibur5.Services.Strategy;

namespace Excalibur5.Tests;

internal static class RecoveryControllerTests
{
    // Martingale: a loss advances the ladder and uses the supplied stake; a win resets to base.
    public static Task MartingaleAdvancesOnLossResetsOnWin()
    {
        var c = new RecoveryController();
        c.Start(10m);

        // level 1 → calculateStake(1)
        var s1 = c.RegisterMartingaleResult(isLoss: true, maxLevel: 3, level => 10m * level);
        TestAssert.Equal(10m, s1, "Level 1 stake");

        var s2 = c.RegisterMartingaleResult(isLoss: true, maxLevel: 3, level => 10m * level);
        TestAssert.Equal(20m, s2, "Level 2 stake");

        // win resets to base
        var s3 = c.RegisterMartingaleResult(isLoss: false, maxLevel: 3, level => 10m * level);
        TestAssert.Equal(10m, s3, "Win resets to base stake");
        return Task.CompletedTask;
    }

    // Martingale: at max level a further loss resets to base (ladder capped).
    public static Task MartingaleCapsAtMaxLevel()
    {
        var c = new RecoveryController();
        c.Start(10m);

        c.RegisterMartingaleResult(true, maxLevel: 1, level => 10m * level); // level 1
        var capped = c.RegisterMartingaleResult(true, maxLevel: 1, level => 10m * level);
        TestAssert.Equal(10m, capped, "Beyond max level resets to base");
        return Task.CompletedTask;
    }

    // Deficit: a loss accumulates deficit and raises the next stake; recovering clears it.
    public static Task DeficitAccumulatesThenRecovers()
    {
        var c = new RecoveryController();
        c.Start(10m);

        // loss of 10 → deficit 10, no payout samples yet → ratio default 0.5
        // needed = 10 / (0.5 * 1) = 20, clamped to max 50
        var afterLoss = c.RegisterDeficitResult(isLoss: true, profit: -10m, recoveryTrades: 1, maxStake: 50m);
        TestAssert.Equal(20m, afterLoss, "Deficit should raise stake");

        // win of 20 → deficit cleared → back to base
        var afterWin = c.RegisterDeficitResult(isLoss: false, profit: 20m, recoveryTrades: 1, maxStake: 50m);
        TestAssert.Equal(10m, afterWin, "Cleared deficit returns to base stake");
        return Task.CompletedTask;
    }

    public static Task ResetProgressClearsLadderAndDeficit()
    {
        var c = new RecoveryController();
        c.Start(10m);
        c.RegisterMartingaleResult(true, 3, level => 10m * level);
        TestAssert.True(c.HasActiveProgress, "Should have active progress after a loss");

        c.ResetProgress();
        TestAssert.False(c.HasActiveProgress, "ResetProgress should clear progress");
        return Task.CompletedTask;
    }
}
