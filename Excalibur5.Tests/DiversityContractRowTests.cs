using Excalibur5.Models;
using Excalibur5.ViewModels.Diversity;

namespace Excalibur5.Tests;

internal static class DiversityContractRowTests
{
    public static Task DigitOverRowMapsToLegWithDigitBarrier()
    {
        var row = new DiversityContractRow
        {
            Symbol = "R_25",
            ContractType = "DIGITOVER",
            BarrierDigit = 4,
            StakeText = "2.50"
        };
        var leg = row.ToLeg();
        TestAssert.Equal("R_25", leg.Symbol, "Symbol mapped");
        TestAssert.Equal("DIGITOVER", leg.ContractType, "Type mapped");
        TestAssert.Equal("4", leg.Barrier ?? "", "Digit barrier mapped as string");
        TestAssert.Equal(2.50m, leg.Stake, "Stake parsed");
        TestAssert.Equal("t", leg.DurationUnit, "Digit contracts use ticks");
        return Task.CompletedTask;
    }

    public static Task DigitContractDefaultsToTicksOnly()
    {
        var row = new DiversityContractRow { ContractType = "DIGITUNDER" };
        TestAssert.True(row.AvailableDurationUnits.Count == 1
            && row.AvailableDurationUnits[0] == DurationUnitType.Ticks,
            "Digit contracts only allow Ticks");
        return Task.CompletedTask;
    }

    public static Task SwitchingToRiseFallOffersMoreUnits()
    {
        var row = new DiversityContractRow { ContractType = "DIGITOVER" };
        row.ContractType = "CALL"; // triggers OnContractTypeChanged
        TestAssert.True(row.AvailableDurationUnits.Contains(DurationUnitType.Seconds),
            "Rise/Fall should offer Seconds");
        TestAssert.True(row.AvailableDurationUnits.Contains(DurationUnitType.Minutes),
            "Rise/Fall should offer Minutes");
        return Task.CompletedTask;
    }

    public static Task TicksDurationClampedToTen()
    {
        var row = new DiversityContractRow { ContractType = "DIGITOVER", DurationText = "25" };
        TestAssert.Equal(10, row.DurationValue, "Ticks duration clamps to 10");
        return Task.CompletedTask;
    }

    public static Task NonDigitContractHasNoBarrier()
    {
        var row = new DiversityContractRow { ContractType = "CALL", BarrierDigit = 5 };
        var leg = row.ToLeg();
        TestAssert.True(leg.Barrier == null, "Rise/Fall leg should have no digit barrier");
        return Task.CompletedTask;
    }

    public static Task SecondsDurationMapsToCorrectUnitCode()
    {
        var row = new DiversityContractRow { ContractType = "CALL" };
        row.DurationUnit = DurationUnitType.Seconds;
        row.DurationText = "30";
        var leg = row.ToLeg();
        TestAssert.Equal("s", leg.DurationUnit, "Seconds maps to 's'");
        TestAssert.Equal(30, leg.DurationTicks, "Duration value preserved");
        return Task.CompletedTask;
    }
}
