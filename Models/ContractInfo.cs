namespace Excalibur5.Models;

public sealed class ContractInfo
{
    public string ContractType { get; init; } = string.Empty;
    public int MinDuration { get; init; }
    public string MinDurationUnit { get; init; } = string.Empty;
    public int MaxDuration { get; init; }
    public string MaxDurationUnit { get; init; } = string.Empty;
    public decimal MinStake { get; init; }
    public decimal MaxStake { get; init; }
    public decimal? Barrier { get; init; }
    public string BarrierCategory { get; init; } = string.Empty;
}
