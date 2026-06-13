namespace Excalibur5.Models;

/// <summary>
/// Digit Over/Under contracts (DIGITOVER / DIGITUNDER). For digit contracts the broker's
/// <c>barrier</c> is the predicted last digit (0-9), not a price. Each Diversity leg picks one
/// side (Over or Under) plus a barrier digit, so unlike Rise/Fall this strategy exposes both
/// contract types and the leg config chooses which to use.
/// Digit contracts run on tick durations only.
/// </summary>
public sealed class DigitOverUnderContractStrategy : IContractTypeStrategy
{
    public string DisplayName => "Digits Over/Under";
    public string CallContractType => "DIGITOVER";
    public string PutContractType => "DIGITUNDER";
    public string CallButtonLabel => "▲ Over";
    public string PutButtonLabel => "▼ Under";
    public bool RequiresBarrier => true;
    public ContractCategory Category => ContractCategory.Digits;

    public IReadOnlyList<DurationUnitType> AvailableDurationUnits { get; } =
        [DurationUnitType.Ticks];

    public override string ToString() => DisplayName;
}
