namespace Excalibur5.Services.Strategy.Diversity;

/// <summary>
/// Pure decision for Diversity signal mode: given the current state and an incoming signal's
/// confidence, decides whether to fire a group now. Keeps the live wiring trivial and testable —
/// no firing while signal mode is off, while disabled, below the confidence threshold, or while a
/// group is still pending (one group at a time).
/// </summary>
public static class DiversitySignalGate
{
    public static bool ShouldFire(
        bool signalModeEnabled,
        double signalConfidence,
        double confidenceThreshold,
        int pendingGroupCount,
        bool isBusy)
    {
        if (!signalModeEnabled) return false;
        if (isBusy) return false;
        if (pendingGroupCount > 0) return false; // one group at a time
        if (signalConfidence < confidenceThreshold) return false;
        return true;
    }
}
