namespace Excalibur5.Models;

public sealed class ProfitTableEntry
{
    public long TransactionId { get; init; }
    public long ContractId { get; init; }
    public string ContractType { get; init; } = string.Empty;
    public string Shortcode { get; init; } = string.Empty;
    public string Longcode { get; init; } = string.Empty;
    public decimal BuyPrice { get; init; }
    public decimal SellPrice { get; init; }
    public long PurchaseTime { get; init; }
    public long SellTime { get; init; }
    public decimal ProfitLoss { get; init; }
    public string EntrySpot { get; init; } = string.Empty;
    public string ExitSpot { get; init; } = string.Empty;
}
