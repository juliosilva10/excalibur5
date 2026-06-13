using Excalibur5.Services.Strategy;

namespace Excalibur5.Tests;

internal static class RecoveryStakeTests
{
    public static Task AverageDefaultsWhenNoSamples()
    {
        var avg = RecoveryStakeCalculator.AveragePayoutRatio(new decimal[5], 0);
        TestAssert.Equal(0.5m, avg, "No samples should default to 0.5");
        return Task.CompletedTask;
    }

    public static Task AverageComputesOverSamples()
    {
        var ratios = new decimal[] { 0.8m, 0.9m, 1.0m, 0m, 0m };
        var avg = RecoveryStakeCalculator.AveragePayoutRatio(ratios, 3);
        TestAssert.Equal(0.9m, avg, "Average of 0.8/0.9/1.0 should be 0.9");
        return Task.CompletedTask;
    }

    public static Task NextStakeReturnsBaseWhenNoDeficit()
    {
        var stake = RecoveryStakeCalculator.NextStake(
            deficit: 0m, avgPayoutRatio: 0.9m, recoveryTrades: 1, baseStake: 10m, maxStake: 50m);
        TestAssert.Equal(10m, stake, "No deficit → base stake");
        return Task.CompletedTask;
    }

    public static Task NextStakeRecoversDeficit()
    {
        // deficit 9, ratio 0.9, 1 trade → needed 10; max(10, base 10) = 10
        var stake = RecoveryStakeCalculator.NextStake(
            deficit: 9m, avgPayoutRatio: 0.9m, recoveryTrades: 1, baseStake: 10m, maxStake: 50m);
        TestAssert.Equal(10m, stake, "Should size to recover the deficit");
        return Task.CompletedTask;
    }

    public static Task NextStakeClampedToMax()
    {
        // deficit 100, ratio 0.5, 1 trade → needed 200; clamped to maxStake 50
        var stake = RecoveryStakeCalculator.NextStake(
            deficit: 100m, avgPayoutRatio: 0.5m, recoveryTrades: 1, baseStake: 10m, maxStake: 50m);
        TestAssert.Equal(50m, stake, "Should clamp to max stake");
        return Task.CompletedTask;
    }

    public static Task NextStakeNeverBelowBase()
    {
        // deficit 1, ratio 0.9, 1 trade → needed ~1.11; floored to base 10
        var stake = RecoveryStakeCalculator.NextStake(
            deficit: 1m, avgPayoutRatio: 0.9m, recoveryTrades: 1, baseStake: 10m, maxStake: 50m);
        TestAssert.Equal(10m, stake, "Should never go below base stake");
        return Task.CompletedTask;
    }
}
