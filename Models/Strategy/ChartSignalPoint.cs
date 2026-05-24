namespace Excalibur5.Models.Strategy;

public class ChartSignalPoint
{
    public double Price { get; set; }
    public long Timestamp { get; set; }
    public SignalDirection Direction { get; set; }
    public SignalType Type { get; set; }
    public string Label { get; set; } = string.Empty;
}

public enum SignalType
{
    Buy,
    Sell,
    Alert
}