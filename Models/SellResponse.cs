namespace Excalibur5.Models;

public sealed class SellResponse
{
    public long ContractId { get; init; }
    public decimal SoldFor { get; init; }
    public string Error { get; init; } = string.Empty;
    public bool Success => string.IsNullOrEmpty(Error);
}
