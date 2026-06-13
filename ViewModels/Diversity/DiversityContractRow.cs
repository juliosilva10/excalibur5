using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Excalibur5.Models;
using Excalibur5.Models.Diversity;

namespace Excalibur5.ViewModels.Diversity;

/// <summary>An option for the contract-type combobox: the broker code plus a friendly label.</summary>
public sealed record ContractTypeOption(string Value, string Display)
{
    public override string ToString() => Display;
}

/// <summary>
/// Editable, observable representation of one Diversity contract ("perna" internally) in the panel.
/// Exposes combobox-friendly sources (markets, contract types, digits, duration units) populated
/// from the same data the rest of the app uses, and converts to the immutable
/// <see cref="DiversityLeg"/> for execution. Barrier is the digit prediction (0-9) for digit
/// contracts or a price barrier string otherwise.
/// </summary>
public partial class DiversityContractRow : ObservableObject
{
    /// <summary>Markets shared with the rest of the app (MarketInfo.SyntheticMarkets).</summary>
    public static IReadOnlyList<MarketInfo> Markets => MarketInfo.SyntheticMarkets;

    /// <summary>Contract types offered for a Diversity contract.</summary>
    public static IReadOnlyList<ContractTypeOption> ContractTypes { get; } = new[]
    {
        new ContractTypeOption("DIGITOVER", "Digit Over"),
        new ContractTypeOption("DIGITUNDER", "Digit Under"),
        new ContractTypeOption("DIGITEVEN", "Digit Even"),
        new ContractTypeOption("DIGITODD", "Digit Odd"),
        new ContractTypeOption("DIGITMATCH", "Digit Match"),
        new ContractTypeOption("DIGITDIFF", "Digit Diff"),
        new ContractTypeOption("CALL", "Rise"),
        new ContractTypeOption("PUT", "Fall"),
    };

    /// <summary>Valid last digits 0-9 for digit barriers.</summary>
    public static IReadOnlyList<int> Digits { get; } = Enumerable.Range(0, 10).ToList();

    /// <summary>Duration units allowed for the currently selected contract type.</summary>
    public ObservableCollection<DurationUnitType> AvailableDurationUnits { get; } = new();

    [ObservableProperty] private string _symbol = "R_10";
    [ObservableProperty] private string _contractType = "DIGITOVER";
    [ObservableProperty] private int _barrierDigit = 5;
    [ObservableProperty] private string _stakeText = "1.00";
    [ObservableProperty] private DurationUnitType _durationUnit = DurationUnitType.Ticks;
    [ObservableProperty] private string _durationText = "1";

    public DiversityContractRow()
    {
        RefreshDurationUnits();
    }

    public decimal Stake =>
        decimal.TryParse(StakeText, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0 ? v : 0m;

    /// <summary>Parsed duration value (defaults to 1, clamped to the unit's sane range).</summary>
    public int DurationValue
    {
        get
        {
            if (!int.TryParse(DurationText, out var v) || v < 1) v = 1;
            return DurationUnit == DurationUnitType.Ticks && v > 10 ? 10 : v;
        }
    }

    /// <summary>True if this row maps to a digit Over/Under contract (barrier = digit).</summary>
    public bool IsDigitOverUnder => ContractType is "DIGITOVER" or "DIGITUNDER";

    /// <summary>True if this contract type needs a barrier digit at all.</summary>
    public bool RequiresDigit => ContractType is "DIGITOVER" or "DIGITUNDER" or "DIGITMATCH" or "DIGITDIFF";

    partial void OnContractTypeChanged(string value)
    {
        RefreshDurationUnits();
        OnPropertyChanged(nameof(IsDigitOverUnder));
        OnPropertyChanged(nameof(RequiresDigit));
    }

    private void RefreshDurationUnits()
    {
        // Use the same source of truth as the rest of the app: the contract-type strategy.
        IReadOnlyList<DurationUnitType> units = ContractType switch
        {
            "DIGITOVER" or "DIGITUNDER" or "DIGITEVEN" or "DIGITODD" or "DIGITMATCH" or "DIGITDIFF"
                => new DigitOverUnderContractStrategy().AvailableDurationUnits,
            _ => new RiseFallContractStrategy().AvailableDurationUnits
        };

        AvailableDurationUnits.Clear();
        foreach (var u in units) AvailableDurationUnits.Add(u);
        if (!units.Contains(DurationUnit))
            DurationUnit = units.Count > 0 ? units[0] : DurationUnitType.Ticks;
    }

    private static string DurationUnitCode(DurationUnitType unit) => unit switch
    {
        DurationUnitType.Ticks => "t",
        DurationUnitType.Seconds => "s",
        DurationUnitType.Minutes => "m",
        DurationUnitType.Hours => "h",
        DurationUnitType.Days => "d",
        _ => "t"
    };

    public DiversityLeg ToLeg() => new()
    {
        Symbol = Symbol.Trim(),
        ContractType = ContractType.Trim(),
        Barrier = RequiresDigit ? BarrierDigit.ToString() : null,
        Stake = Stake,
        DurationTicks = DurationValue,
        DurationUnit = DurationUnitCode(DurationUnit),
        Label = $"{ContractTypeFormatter.ToDisplayLabel(ContractType)}"
              + (RequiresDigit ? $" {BarrierDigit}" : "")
              + $" @ {Symbol}"
    };

    /// <summary>The digit prediction if this is a valid digit over/under row; null otherwise.</summary>
    public DigitPrediction? ToDigitPrediction()
    {
        if (!IsDigitOverUnder) return null;
        var side = ContractType == "DIGITOVER" ? DigitSide.Over : DigitSide.Under;
        return new DigitPrediction(side, BarrierDigit);
    }
}
