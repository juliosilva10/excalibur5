namespace Excalibur5.Models;

public sealed class RiseFallContractStrategy : IContractTypeStrategy
{
    public string DisplayName => "Rise/Fall";
    public string CallContractType => "CALL";
    public string PutContractType => "PUT";
    public string CallButtonLabel => "Rise";
    public string PutButtonLabel => "Fall";
    public bool RequiresBarrier => false;
    public ContractCategory Category => ContractCategory.RiseFall;

    public IReadOnlyList<DurationUnitType> AvailableDurationUnits { get; } =
        [DurationUnitType.Ticks, DurationUnitType.Seconds, DurationUnitType.Minutes];

    public override string ToString() => DisplayName;
}
