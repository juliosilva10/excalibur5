using Excalibur5.Services.Strategy.Diversity;

namespace Excalibur5.Tests;

internal static class DiversitySignalGateTests
{
    public static Task FiresWhenAllConditionsMet()
    {
        TestAssert.True(
            DiversitySignalGate.ShouldFire(signalModeEnabled: true, signalConfidence: 0.8,
                confidenceThreshold: 0.6, pendingGroupCount: 0, isBusy: false),
            "Should fire when enabled, confident, idle, no pending group");
        return Task.CompletedTask;
    }

    public static Task DoesNotFireWhenSignalModeOff()
    {
        TestAssert.False(
            DiversitySignalGate.ShouldFire(false, 0.9, 0.6, 0, false),
            "Signal mode off → never fire");
        return Task.CompletedTask;
    }

    public static Task DoesNotFireBelowThreshold()
    {
        TestAssert.False(
            DiversitySignalGate.ShouldFire(true, 0.5, 0.6, 0, false),
            "Confidence below threshold → no fire");
        return Task.CompletedTask;
    }

    public static Task DoesNotFireWhilePendingGroup()
    {
        TestAssert.False(
            DiversitySignalGate.ShouldFire(true, 0.9, 0.6, 1, false),
            "A group already pending → one at a time, no fire");
        return Task.CompletedTask;
    }

    public static Task DoesNotFireWhileBusy()
    {
        TestAssert.False(
            DiversitySignalGate.ShouldFire(true, 0.9, 0.6, 0, true),
            "Busy buying → no fire");
        return Task.CompletedTask;
    }
}
