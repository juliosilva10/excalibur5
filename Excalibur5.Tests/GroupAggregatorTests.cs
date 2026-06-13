using Excalibur5.Services.Strategy.Diversity;

namespace Excalibur5.Tests;

internal static class GroupAggregatorTests
{
    // Two legs settling in order; net profit decides Won.
    public static Task TwoLegsConsolidateOnLastSettlement()
    {
        var agg = new GroupResultAggregator();
        var g = Guid.NewGuid();
        agg.RegisterGroup(g, new long[] { 101, 102 }, totalStake: 20m);

        // Over wins +8.5, Under loses -10 → net -1.5 (the over/under reality: one wins, one loses)
        var r1 = agg.RecordLegSettled(101, 8.5m);
        TestAssert.Null(r1, "First leg should not finalize the group");

        var r2 = agg.RecordLegSettled(102, -10m);
        TestAssert.NotNull(r2, "Second leg should finalize the group");
        TestAssert.True(Math.Abs(r2!.TotalProfit - (-1.5m)) < 0.0001m, "Net profit should be -1.5");
        TestAssert.False(r2.Won, "Negative net → not won");
        TestAssert.Equal(20m, r2.TotalStake, "Total stake preserved");
        return Task.CompletedTask;
    }

    // Settlements arriving out of order must still consolidate correctly.
    public static Task SettlesOutOfOrder()
    {
        var agg = new GroupResultAggregator();
        var g = Guid.NewGuid();
        agg.RegisterGroup(g, new long[] { 1, 2, 3 }, totalStake: 30m);

        TestAssert.Null(agg.RecordLegSettled(3, 5m), "leg 3 first");
        TestAssert.Null(agg.RecordLegSettled(1, 5m), "leg 1 second");
        var r = agg.RecordLegSettled(2, 5m);
        TestAssert.NotNull(r, "leg 2 last → finalize");
        TestAssert.Equal(15m, r!.TotalProfit, "3x +5 = +15");
        TestAssert.True(r.Won, "Positive net → won");
        return Task.CompletedTask;
    }

    // A duplicate settlement for an already-recorded leg is ignored.
    public static Task DuplicateSettlementIgnored()
    {
        var agg = new GroupResultAggregator();
        var g = Guid.NewGuid();
        agg.RegisterGroup(g, new long[] { 7, 8 }, totalStake: 20m);

        agg.RecordLegSettled(7, 4m);
        var dup = agg.RecordLegSettled(7, 999m); // duplicate → ignored, not finalize
        TestAssert.Null(dup, "Duplicate leg settlement must not finalize");

        var r = agg.RecordLegSettled(8, 4m);
        TestAssert.NotNull(r, "Real second leg finalizes");
        TestAssert.Equal(8m, r!.TotalProfit, "Duplicate's 999 must be ignored; 4+4=8");
        return Task.CompletedTask;
    }

    // A stuck leg is force-settled (e.g. on timeout) with a supplied profit.
    public static Task ForceSettleResolvesStuckLeg()
    {
        var agg = new GroupResultAggregator();
        var g = Guid.NewGuid();
        agg.RegisterGroup(g, new long[] { 50, 51 }, totalStake: 20m);

        agg.RecordLegSettled(50, 6m); // leg 51 never settles
        var r = agg.ForceSettleRemaining(g, profitForUnsettled: -10m);
        TestAssert.NotNull(r, "Force settle should finalize");
        TestAssert.Equal(-4m, r!.TotalProfit, "6 + (-10) = -4");
        TestAssert.Equal(0, agg.PendingGroupCount, "No pending groups after finalize");
        return Task.CompletedTask;
    }

    public static Task UnknownContractReturnsNull()
    {
        var agg = new GroupResultAggregator();
        TestAssert.Null(agg.RecordLegSettled(9999, 1m), "Unknown contract → null");
        TestAssert.False(agg.IsGroupContract(9999), "Unknown contract is not a group contract");
        return Task.CompletedTask;
    }

    public static Task CleansUpAfterFinalize()
    {
        var agg = new GroupResultAggregator();
        var g = Guid.NewGuid();
        agg.RegisterGroup(g, new long[] { 200, 201 }, totalStake: 20m);
        TestAssert.True(agg.IsGroupContract(200), "Contract tracked while pending");

        agg.RecordLegSettled(200, 1m);
        agg.RecordLegSettled(201, 1m);
        TestAssert.False(agg.IsGroupContract(200), "Contract untracked after finalize");
        TestAssert.Null(agg.ForceSettleRemaining(g, 0m), "Finalized group is unknown");
        return Task.CompletedTask;
    }
}
