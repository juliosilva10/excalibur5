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
    ("Market prices use selected pip size", PriceFormattingTests.UsesSelectedPipSize)
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
