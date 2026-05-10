namespace Excalibur5.Models;

public sealed class AuthorizeResponse
{
    public string  LoginId     { get; init; } = string.Empty;
    public bool    IsVirtual   { get; init; }
    public decimal Balance     { get; init; }
    public string  Currency    { get; init; } = string.Empty;
    public string  FullName    { get; init; } = string.Empty;
}
