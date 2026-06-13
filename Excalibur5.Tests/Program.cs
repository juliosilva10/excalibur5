using Excalibur5.Tests;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Disabled settings keep entries real", EntryModeTests.DisabledSettingsKeepEntriesReal),
    ("Target sequence activates real mode", EntryModeTests.TargetSequenceActivatesRealMode),
    ("Sequence works without tolerance", EntryModeTests.SequenceWorksWithoutTolerance),
    ("Only one global virtual entry is reserved", EntryModeTests.OnlyOneVirtualEntryIsReserved),
    ("Real win resets loss count", EntryModeTests.RealWinResetsLossCount),
    ("Old real cycle is ignored", EntryModeTests.OldRealCycleIsIgnored),
    ("Sequence triggers after returning to virtual", EntryModeTests.SequenceTriggersAgain),
    ("Tick trade uses exact tick count", SimulatorTests.TickTradeUsesExactTickCount),
    ("Only one simulation is pending", SimulatorTests.OnlyOneSimulationIsPending),
    ("Equal final price is a loss", SimulatorTests.EqualFinalPriceIsLoss),
    ("Timed trade remains paused", SimulatorTests.TimedTradeRemainsPaused),
    ("Completed trade carries win profit", SimulatorTests.CompletedTradeCarriesWinProfit),
    ("Market prices use selected pip size", PriceFormattingTests.UsesSelectedPipSize),
    ("Risk: take profit triggers sell", PositionRiskTests.TakeProfitTriggersSell),
    ("Risk: TP waits when not valid to sell", PositionRiskTests.TakeProfitWaitsWhenNotValidToSell),
    ("Risk: trailing stop ratchets at 90%", PositionRiskTests.TrailingStopRatchetsAt90Percent),
    ("Risk: trailing stop breakeven at 70%", PositionRiskTests.TrailingStopMovesToBreakevenAt70Percent),
    ("Risk: stop loss triggers sell", PositionRiskTests.StopLossTriggersSell),
    ("Risk: holds between thresholds", PositionRiskTests.HoldsBetweenThresholds),
    ("Barriers: parse from error message", BarrierTests.ParsesBarriersFromErrorMessage),
    ("Barriers: empty for unrelated message", BarrierTests.ReturnsEmptyForUnrelatedMessage),
    ("Barriers: empty for null/empty", BarrierTests.ReturnsEmptyForNullOrEmpty),
    ("Barriers: fallback empty when no spot", BarrierTests.FallbackOffsetsEmptyWhenNoSpot),
    ("Barriers: duration mode sorted/symmetric", BarrierTests.FallbackOffsetsDurationModeAreSortedAndSymmetric),
    ("Barriers: end-time mode sorted", BarrierTests.FallbackOffsetsEndTimeModeSortedRelativeToSpot),
    ("Recovery: average defaults without samples", RecoveryStakeTests.AverageDefaultsWhenNoSamples),
    ("Recovery: average over samples", RecoveryStakeTests.AverageComputesOverSamples),
    ("Recovery: base stake when no deficit", RecoveryStakeTests.NextStakeReturnsBaseWhenNoDeficit),
    ("Recovery: sizes to recover deficit", RecoveryStakeTests.NextStakeRecoversDeficit),
    ("Recovery: clamped to max stake", RecoveryStakeTests.NextStakeClampedToMax),
    ("Recovery: never below base stake", RecoveryStakeTests.NextStakeNeverBelowBase)
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS: {test.Name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.WriteLine($"FAIL: {test.Name} - {exception.Message}");
    }
}

Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed.");
return failures == 0 ? 0 : 1;
