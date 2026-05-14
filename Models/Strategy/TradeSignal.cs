namespace Excalibur5.Models.Strategy;

public sealed class TradeSignal
{
    public SignalDirection Direction { get; init; }
    public double Confidence { get; init; }
    public string Reason { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public List<IndicatorType> ContributingIndicators { get; init; } = new();
}
