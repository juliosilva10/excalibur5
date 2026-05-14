namespace Excalibur5.Models.Strategy;

public sealed class IndicatorWeight
{
    public IndicatorType Type { get; init; }
    public double BaseWeight { get; init; }
    public double EffectiveWeight { get; set; }
    public double WinRate { get; set; } = 0.5;
}
