using Excalibur5.Models.Diversity;
using Excalibur5.Services.Strategy.Diversity;
using Excalibur5.Services.Strategy.Recovery;

namespace Excalibur5.Tests;

internal static class DiversityRecoveryTests
{
    private static DiversityGroupConfig Group(decimal s1, decimal s2)
        => new()
        {
            Legs = new[]
            {
                new DiversityLeg { Symbol = "R_10", ContractType = "DIGITOVER", Barrier = "5", Stake = s1 },
                new DiversityLeg { Symbol = "R_10", ContractType = "DIGITUNDER", Barrier = "5", Stake = s2 }
            }
        };

    // Distribution keeps each leg proportional to its configured weight and sums to the target.
    public static Task StakeDistributedProportionally()
    {
        var legs = Group(10m, 30m).Legs; // weights 1:3
        var stakes = GroupStakeDistributor.Distribute(legs, targetTotal: 80m);
        TestAssert.Equal(20m, stakes[0], "Leg 1 should get 1/4 of 80");
        TestAssert.Equal(60m, stakes[1], "Leg 2 should get 3/4 of 80");
        return Task.CompletedTask;
    }

    public static Task StakeRespectsMinimum()
    {
        var legs = Group(1m, 1m).Legs;
        var stakes = GroupStakeDistributor.Distribute(legs, targetTotal: 0.40m, minStake: 0.35m);
        TestAssert.True(stakes[0] >= 0.35m && stakes[1] >= 0.35m, "Each leg respects the minimum");
        return Task.CompletedTask;
    }

    // Sum matches target after rounding reconciliation onto the largest leg.
    public static Task StakeSumMatchesTargetAfterRounding()
    {
        var legs = Group(1m, 2m).Legs; // 1:2, target that doesn't divide cleanly
        var stakes = GroupStakeDistributor.Distribute(legs, targetTotal: 10m);
        var sum = stakes[0] + stakes[1];
        TestAssert.Equal(10m, sum, "Distributed stakes should sum exactly to the target");
        return Task.CompletedTask;
    }

    // The recover strategy sees the group as one contract: net loss escalates the next total stake.
    public static Task RecoverEscalatesAfterGroupLoss()
    {
        var recover = new MartingaleRecoverStrategy(factor: 2m, maxLevel: 3);
        var bridge = new DiversityRecoveryBridge(recover, baseTotalStake: 20m);

        // Group net loss → record, then next total stake should escalate (x2).
        var lossResult = new GroupResult(Guid.NewGuid(), TotalProfit: -20m, TotalStake: 20m, Won: false, ContractIds: new long[] { 1, 2 });
        bridge.RecordGroupResult(lossResult);

        var next = bridge.NextTotalStake(currentTotalStake: 20m);
        TestAssert.Equal(40m, next, "After a group loss, next total stake doubles (martingale)");
        return Task.CompletedTask;
    }

    public static Task RecoverResetsAfterGroupWin()
    {
        var recover = new MartingaleRecoverStrategy(factor: 2m, maxLevel: 3);
        var bridge = new DiversityRecoveryBridge(recover, baseTotalStake: 20m);

        bridge.RecordGroupResult(new GroupResult(Guid.NewGuid(), -20m, 20m, false, new long[] { 1, 2 }));
        bridge.RecordGroupResult(new GroupResult(Guid.NewGuid(), 15m, 20m, true, new long[] { 3, 4 }));

        var next = bridge.NextTotalStake(20m);
        TestAssert.Equal(20m, next, "After a group win, next total stake returns to base");
        return Task.CompletedTask;
    }

    // BuildNextGroup redistributes the escalated total across legs, preserving non-stake fields.
    public static Task BuildNextGroupRedistributesEscalatedStake()
    {
        var recover = new MartingaleRecoverStrategy(factor: 2m, maxLevel: 3);
        var template = Group(10m, 10m); // base total 20
        var bridge = new DiversityRecoveryBridge(recover, baseTotalStake: 20m);

        bridge.RecordGroupResult(new GroupResult(Guid.NewGuid(), -20m, 20m, false, new long[] { 1, 2 }));
        var next = bridge.BuildNextGroup(template, currentTotalStake: 20m);

        TestAssert.Equal(40m, next.TotalStake, "Next group total escalated to 40");
        TestAssert.Equal(20m, next.Legs[0].Stake, "Even split → 20 each");
        TestAssert.Equal("DIGITOVER", next.Legs[0].ContractType, "Non-stake fields preserved");
        return Task.CompletedTask;
    }

    // Virtual: the group nets to a single W/L for the sequence.
    public static Task VirtualGroupNetsToSingleOutcome()
    {
        TestAssert.True(GroupVirtualEvaluator.GroupWon(new[] { 8m, -5m }), "Net +3 → win");
        TestAssert.False(GroupVirtualEvaluator.GroupWon(new[] { 8m, -10m }), "Net -2 → loss");
        TestAssert.Equal(-2m, GroupVirtualEvaluator.NetProfit(new[] { 8m, -10m }), "Net profit summed");
        return Task.CompletedTask;
    }

    // No recover active → next stake is just the base total.
    public static Task NoRecoverUsesBaseTotal()
    {
        var bridge = new DiversityRecoveryBridge(null, baseTotalStake: 25m);
        TestAssert.Equal(25m, bridge.NextTotalStake(25m), "Without recover, stake stays at base total");
        return Task.CompletedTask;
    }
}
