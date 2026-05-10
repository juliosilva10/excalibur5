namespace Excalibur5.Models;

public sealed class BalanceResponse
{
    public decimal Balance  { get; init; }
    public string  Currency { get; init; } = string.Empty;
    public string  LoginId  { get; init; } = string.Empty;
}
