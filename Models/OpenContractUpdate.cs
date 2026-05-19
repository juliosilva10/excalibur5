namespace Excalibur5.Models;

public sealed class OpenContractUpdate
{
    public long ContractId { get; init; }
    public string Symbol { get; init; } = string.Empty;
    public string ContractType { get; init; } = string.Empty;
    public decimal BuyPrice { get; init; }
    public decimal BidPrice { get; init; }
    public decimal CurrentSpot { get; init; }
    public decimal EntrySpot { get; init; }
    public string EntrySpotRaw { get; init; } = string.Empty;
    public string ExitSpotRaw { get; init; } = string.Empty;
    public decimal Profit { get; init; }
    public long DateStart { get; init; }
    public long DateExpiry { get; init; }
    public long EntryTickTime { get; init; }
    public bool IsExpired { get; init; }
    public bool IsSold { get; init; }
    public bool IsValidToSell { get; init; }
    public long SellTime { get; init; }
    public string Status { get; init; } = string.Empty;
    public string SubscriptionId { get; init; } = string.Empty;
}
