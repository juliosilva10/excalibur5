using CommunityToolkit.Mvvm.ComponentModel;
using Excalibur5.Models.Diversity;

namespace Excalibur5.ViewModels.Diversity;

/// <summary>
/// Editable, observable representation of one Diversity leg in the panel. Converts to the immutable
/// <see cref="DiversityLeg"/> for execution. Barrier is the digit prediction (0-9) for digit
/// contracts or a price barrier string otherwise.
/// </summary>
public partial class DiversityLegRow : ObservableObject
{
    [ObservableProperty] private string _symbol = "R_10";
    [ObservableProperty] private string _contractType = "DIGITOVER";
    [ObservableProperty] private string _barrier = "5";
    [ObservableProperty] private string _stakeText = "1.00";
    [ObservableProperty] private int _durationTicks = 1;

    public decimal Stake =>
        decimal.TryParse(StakeText, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0 ? v : 0m;

    public DiversityLeg ToLeg() => new()
    {
        Symbol = Symbol.Trim(),
        ContractType = ContractType.Trim(),
        Barrier = string.IsNullOrWhiteSpace(Barrier) ? null : Barrier.Trim(),
        Stake = Stake,
        DurationTicks = DurationTicks < 1 ? 1 : DurationTicks,
        Label = $"{ContractType} {Barrier} @ {Symbol}"
    };

    /// <summary>True if this row maps to a digit Over/Under contract.</summary>
    public bool IsDigitOverUnder => ContractType is "DIGITOVER" or "DIGITUNDER";

    /// <summary>The digit prediction if this is a valid digit row; null otherwise.</summary>
    public DigitPrediction? ToDigitPrediction()
    {
        if (!IsDigitOverUnder) return null;
        if (!int.TryParse(Barrier, out var digit)) return null;
        var side = ContractType == "DIGITOVER" ? DigitSide.Over : DigitSide.Under;
        return new DigitPrediction(side, digit);
    }
}
