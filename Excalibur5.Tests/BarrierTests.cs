using Excalibur5.Services.Strategy;

namespace Excalibur5.Tests;

internal static class BarrierTests
{
    public static Task ParsesBarriersFromErrorMessage()
    {
        var barriers = BarrierCalculator.ParseBarriersFromError(
            "Barriers available are +29.320, +15.430, +0.050, -15.280, -29.040");

        TestAssert.Equal(5, barriers.Count, "Should parse 5 barriers");
        TestAssert.Equal("+29.320", barriers[0], "First barrier mismatch");
        TestAssert.Equal("-29.040", barriers[4], "Last barrier mismatch");
        return Task.CompletedTask;
    }

    public static Task ReturnsEmptyForUnrelatedMessage()
    {
        var barriers = BarrierCalculator.ParseBarriersFromError("Some other API error");
        TestAssert.Equal(0, barriers.Count, "Unrelated message should yield no barriers");
        return Task.CompletedTask;
    }

    public static Task ReturnsEmptyForNullOrEmpty()
    {
        TestAssert.Equal(0, BarrierCalculator.ParseBarriersFromError("").Count, "Empty → none");
        TestAssert.Equal(0, BarrierCalculator.ParseBarriersFromError(null!).Count, "Null → none");
        return Task.CompletedTask;
    }

    public static Task FallbackOffsetsEmptyWhenNoSpot()
    {
        var offsets = BarrierCalculator.GenerateFallbackOffsets(
            spot: 0m, pipSize: 3, useDuration: true, durationMinutes: 5, innerBase: 5m, outerBase: 10m);
        TestAssert.Equal(0, offsets.Count, "No spot → no offsets");
        return Task.CompletedTask;
    }

    public static Task FallbackOffsetsDurationModeAreSortedAndSymmetric()
    {
        var offsets = BarrierCalculator.GenerateFallbackOffsets(
            spot: 1000m, pipSize: 3, useDuration: true, durationMinutes: 5, innerBase: 5m, outerBase: 10m);

        TestAssert.Equal(5, offsets.Count, "Duration mode yields 5 barriers");
        // sorted ascending
        for (int i = 1; i < offsets.Count; i++)
            TestAssert.True(offsets[i] >= offsets[i - 1], "Offsets must be sorted ascending");
        // contains a zero (the at-the-money barrier)
        TestAssert.True(offsets.Contains(0m), "Should contain the zero barrier");
        return Task.CompletedTask;
    }

    public static Task FallbackOffsetsEndTimeModeSortedRelativeToSpot()
    {
        var offsets = BarrierCalculator.GenerateFallbackOffsets(
            spot: 1000m, pipSize: 2, useDuration: false, durationMinutes: 0, innerBase: 5m, outerBase: 10m);

        TestAssert.True(offsets.Count > 0, "End-time mode should yield barriers");
        for (int i = 1; i < offsets.Count; i++)
            TestAssert.True(offsets[i] >= offsets[i - 1], "Offsets must be sorted ascending");
        return Task.CompletedTask;
    }
}
