namespace Excalibur5.Models;

public sealed class TradeHistoryItem
{
    public string Operacao { get; init; } = string.Empty;
    public string Estrategia { get; init; } = string.Empty;
    public string Market { get; init; } = string.Empty;
    public string Tipo { get; init; } = string.Empty;
    public string ReferenceNumber { get; init; } = string.Empty;
    public DateTime PurchaseTime { get; init; }
    public decimal Stake { get; init; }
    public DateTime? SellTime { get; init; }
    public string EntrySpot { get; set; } = string.Empty;
    public string ExitSpot { get; set; } = string.Empty;
    public decimal? ContractValue { get; init; }
    public decimal? ProfitLoss { get; init; }
    public long ContractId { get; init; }
}
