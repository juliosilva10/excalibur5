using Excalibur5.Services.Strategy.Virtual;

namespace Excalibur5.Tests;

internal static class EntryModeTests
{
    public static Task DisabledSettingsKeepEntriesReal()
    {
        var controller = new VirtualEntryModeController();
        controller.Start(VirtualEntrySettings.Disabled);

        TestAssert.False(controller.IsVirtualMode, "Disabled filter must start in real mode");
        TestAssert.False(controller.TryReserveVirtualEntry(), "Disabled filter reserved a virtual entry");
        TestAssert.False(controller.RecordVirtualResult(false), "Disabled filter must ignore virtual results");
        return Task.CompletedTask;
    }

    public static Task TargetSequenceActivatesRealMode()
    {
        var controller = StartController("WLLL", tolerance: 2);

        TestAssert.False(RecordVirtual(controller, true), "Partial sequence triggered early");
        TestAssert.False(RecordVirtual(controller, false), "Partial sequence triggered early");
        TestAssert.False(RecordVirtual(controller, false), "Partial sequence triggered early");
        TestAssert.True(RecordVirtual(controller, false), "Complete sequence did not trigger");
        TestAssert.False(controller.IsVirtualMode, "Controller did not enter real mode");
        TestAssert.Equal(1, controller.CurrentRealCycleId, "Unexpected real cycle");
        return Task.CompletedTask;
    }

    public static Task SequenceWorksWithoutTolerance()
    {
        var controller = StartController("L", tolerance: 0);

        TestAssert.True(controller.IsVirtualMode, "Sequence without tolerance did not start virtually");
        TestAssert.True(RecordVirtual(controller, false), "Sequence without tolerance did not trigger");
        TestAssert.False(
            controller.RecordRealResult(controller.CurrentRealCycleId, false),
            "Empty tolerance returned to virtual mode");
        TestAssert.False(controller.IsVirtualMode, "Empty tolerance did not remain in real mode");
        return Task.CompletedTask;
    }

    public static Task OnlyOneVirtualEntryIsReserved()
    {
        var controller = StartController("L", tolerance: 1);

        TestAssert.True(controller.TryReserveVirtualEntry(), "First reservation failed");
        TestAssert.False(controller.TryReserveVirtualEntry(), "Second reservation was accepted");
        controller.CancelVirtualEntry();
        TestAssert.True(controller.TryReserveVirtualEntry(), "Reservation was not released");
        controller.CancelVirtualEntry();
        return Task.CompletedTask;
    }

    public static Task RealWinResetsLossCount()
    {
        var controller = ActivateRealMode(tolerance: 2);
        var cycleId = controller.CurrentRealCycleId;

        TestAssert.False(controller.RecordRealResult(cycleId, false), "First loss ended real mode");
        TestAssert.False(controller.RecordRealResult(cycleId, true), "Win ended real mode");
        TestAssert.False(controller.RecordRealResult(cycleId, false), "Loss count was not reset");
        TestAssert.True(controller.RecordRealResult(cycleId, false), "Tolerance did not end real mode");
        return Task.CompletedTask;
    }

    public static Task OldRealCycleIsIgnored()
    {
        var controller = ActivateRealMode(tolerance: 1);
        var oldCycleId = controller.CurrentRealCycleId;
        TestAssert.True(controller.RecordRealResult(oldCycleId, false), "Tolerance did not return to virtual");

        TestAssert.True(RecordVirtual(controller, false), "New sequence did not trigger");
        var currentCycleId = controller.CurrentRealCycleId;

        TestAssert.False(controller.RecordRealResult(oldCycleId, false), "Old cycle changed current state");
        TestAssert.False(controller.IsVirtualMode, "Old cycle returned controller to virtual");
        TestAssert.True(controller.RecordRealResult(currentCycleId, false), "Current cycle was ignored");
        return Task.CompletedTask;
    }

    public static Task SequenceTriggersAgain()
    {
        var controller = ActivateRealMode(tolerance: 1);
        TestAssert.True(
            controller.RecordRealResult(controller.CurrentRealCycleId, false),
            "Controller did not return to virtual");
        TestAssert.True(RecordVirtual(controller, false), "Sequence did not trigger again");
        TestAssert.Equal(2, controller.CurrentRealCycleId, "Real cycle was not incremented");
        return Task.CompletedTask;
    }

    private static VirtualEntryModeController StartController(string sequence, int tolerance)
    {
        var controller = new VirtualEntryModeController();
        controller.Start(new VirtualEntrySettings(sequence, tolerance));
        return controller;
    }

    private static VirtualEntryModeController ActivateRealMode(int tolerance)
    {
        var controller = StartController("L", tolerance);
        TestAssert.True(RecordVirtual(controller, false), "Setup did not activate real mode");
        return controller;
    }

    private static bool RecordVirtual(VirtualEntryModeController controller, bool won)
    {
        TestAssert.True(controller.TryReserveVirtualEntry(), "Virtual entry was not reserved");
        return controller.RecordVirtualResult(won);
    }
}
