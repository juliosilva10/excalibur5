namespace Excalibur5.Models.Strategy;

public sealed class IndicatorSignal
{
    public IndicatorType Type { get; init; }
    public SignalDirection Direction { get; init; }
    public double Strength { get; init; } // 0.0 to 1.0
    public string Reason { get; init; } = string.Empty;
}
