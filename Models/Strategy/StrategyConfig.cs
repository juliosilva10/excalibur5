namespace Excalibur5.Models.Strategy;

public sealed class StrategyConfig
{
    public int Timeframe { get; set; } = 60; // granularity in seconds
    public SignalDirection AllowedDirection { get; set; } = SignalDirection.None; // None = Both
    public decimal Stake { get; set; } = 10m;
    public decimal TakeProfitUsd { get; set; } = 5m;
    public decimal StopLossUsd { get; set; } = 3m;
    public int MaxConcurrentContracts { get; set; } = 3;
    public double ConfidenceThreshold { get; set; } = 0.70;
    public int DurationMinutes { get; set; } = 5;
    public List<IndicatorType> EnabledIndicators { get; set; } = new()
    {
        IndicatorType.EmaCrossover,
        IndicatorType.Rsi,
        IndicatorType.SupportResistance
    };
}
