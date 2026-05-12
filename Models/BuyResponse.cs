namespace Excalibur5.Models;

public sealed class BuyResponse
{
    public long ContractId { get; init; }
    public decimal BuyPrice { get; init; }
    public decimal Payout { get; init; }
    public long StartTime { get; init; }
    public string LongCode { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;
    public bool Success => string.IsNullOrEmpty(Error);
}
