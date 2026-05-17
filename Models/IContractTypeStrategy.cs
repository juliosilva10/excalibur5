namespace Excalibur5.Models;

public interface IContractTypeStrategy
{
    string DisplayName { get; }
    string CallContractType { get; }
    string PutContractType { get; }
    string CallButtonLabel { get; }
    string PutButtonLabel { get; }
    bool RequiresBarrier { get; }
    IReadOnlyList<DurationUnitType> AvailableDurationUnits { get; }
    ContractCategory Category { get; }
}
