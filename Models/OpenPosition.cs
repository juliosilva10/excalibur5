namespace Excalibur5.Models;

public sealed class OpenPosition
{
    public long ContractId { get; init; }
    public string Symbol { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string ContractType { get; init; } = string.Empty;
    public decimal BuyPrice { get; init; }
    public decimal EntrySpot { get; init; }
    public long DateStart { get; init; }
    public long DateExpiry { get; init; }
}
