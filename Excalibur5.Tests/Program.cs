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
    ("Recovery: never below base stake", RecoveryStakeTests.NextStakeNeverBelowBase),
    ("Controller: martingale advance/reset", RecoveryControllerTests.MartingaleAdvancesOnLossResetsOnWin),
    ("Controller: martingale caps at max", RecoveryControllerTests.MartingaleCapsAtMaxLevel),
    ("Controller: deficit accumulate/recover", RecoveryControllerTests.DeficitAccumulatesThenRecovers),
    ("Controller: reset clears progress", RecoveryControllerTests.ResetProgressClearsLadderAndDeficit),
    ("Digits: win probability matches rule", DigitOddsTests.WinProbabilityMatchesDigitRule),
    ("Digits: barrier ranges enforced", DigitOddsTests.BarrierRangesEnforced),
    ("Digits: Over5/Under5 has dead digit", DigitOddsTests.OverUnderSameBarrierHasDeadDigit),
    ("Digits: overlapping barriers full coverage", DigitOddsTests.OverlappingBarriersAreFullCoverage),
    ("Digits: wins respects side", DigitOddsTests.WinsRespectsSide),
    ("Group: two legs consolidate", GroupAggregatorTests.TwoLegsConsolidateOnLastSettlement),
    ("Group: settles out of order", GroupAggregatorTests.SettlesOutOfOrder),
    ("Group: duplicate settlement ignored", GroupAggregatorTests.DuplicateSettlementIgnored),
    ("Group: force settle stuck leg", GroupAggregatorTests.ForceSettleResolvesStuckLeg),
    ("Group: unknown contract null", GroupAggregatorTests.UnknownContractReturnsNull),
    ("Group: cleans up after finalize", GroupAggregatorTests.CleansUpAfterFinalize),
    ("Coordinator: buys all legs and consolidates", DiversityCoordinatorTests.BuysAllLegsAndConsolidates),
    ("Coordinator: partial failure unwinds", DiversityCoordinatorTests.PartialFailureUnwindsBoughtLegs),
    ("Coordinator: first leg failure buys nothing", DiversityCoordinatorTests.FirstLegFailureBuysNothing),
    ("DivRecover: stake distributed proportionally", DiversityRecoveryTests.StakeDistributedProportionally),
    ("DivRecover: stake respects minimum", DiversityRecoveryTests.StakeRespectsMinimum),
    ("DivRecover: stake sum matches target", DiversityRecoveryTests.StakeSumMatchesTargetAfterRounding),
    ("DivRecover: escalates after group loss", DiversityRecoveryTests.RecoverEscalatesAfterGroupLoss),
    ("DivRecover: resets after group win", DiversityRecoveryTests.RecoverResetsAfterGroupWin),
    ("DivRecover: builds next group redistributed", DiversityRecoveryTests.BuildNextGroupRedistributesEscalatedStake),
    ("DivRecover: virtual group nets to one W/L", DiversityRecoveryTests.VirtualGroupNetsToSingleOutcome),
    ("DivRecover: no recover uses base total", DiversityRecoveryTests.NoRecoverUsesBaseTotal)
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
