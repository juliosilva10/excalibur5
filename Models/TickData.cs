namespace Excalibur5.Models;

public enum TickDirection
{
    Flat,
    Up,
    Down
}

public sealed class TickData
{
    public string        Symbol    { get; init; } = string.Empty;
    public decimal       Quote     { get; init; }
    public string        QuoteRaw  { get; init; } = string.Empty;
    public long          Epoch     { get; init; }
    public TickDirection Direction { get; init; }
}
