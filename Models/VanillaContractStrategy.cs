namespace Excalibur5.Models;

public sealed class VanillaContractStrategy : IContractTypeStrategy
{
    public string DisplayName => "Vanilla Call/Put";
    public string CallContractType => "VANILLALONGCALL";
    public string PutContractType => "VANILLALONGPUT";
    public string CallButtonLabel => "Comprar Call";
    public string PutButtonLabel => "Comprar Put";
    public bool RequiresBarrier => true;
    public ContractCategory Category => ContractCategory.Vanillas;

    public IReadOnlyList<DurationUnitType> AvailableDurationUnits { get; } =
        [DurationUnitType.Minutes, DurationUnitType.Hours, DurationUnitType.Days];

    public override string ToString() => DisplayName;
}
