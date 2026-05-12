namespace Excalibur5.Models;

public sealed class ContractsForResponse
{
    public List<ContractInfo> Available { get; init; } = new();
    public decimal Spot { get; init; }
    public string Currency { get; init; } = string.Empty;
    public List<decimal> AvailableBarriers { get; init; } = new();
}
