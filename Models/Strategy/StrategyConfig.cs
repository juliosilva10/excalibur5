namespace Excalibur5.Models.Strategy;

public sealed class StrategyConfig
{
    public SignalDirection AllowedDirection { get; set; } = SignalDirection.None; // None = Both
    public decimal Stake { get; set; } = 10m;
    public decimal TakeProfitUsd { get; set; } = 5m;
    public decimal StopLossUsd { get; set; } = 3m;
    public int MaxConcurrentContracts { get; set; } = 3;
    public double ConfidenceThreshold { get; set; } = 0.70;
    public int DurationSeconds { get; set; } = 60;
    public bool EnableTrailingStop { get; set; } = true;
    public string RecoverMode { get; set; } = string.Empty;
    public decimal MartingaleFactor { get; set; } = 2.0m;
    public int MartingaleMaxLevel { get; set; } = 3;
    public decimal DeficitMaxStake { get; set; } = 50m;
    public int DeficitRecoveryTrades { get; set; } = 1;
    public string Barrier { get; set; } = "+0.000";
    public string StrategyMode { get; set; } = "Multi-Indicador";
    public int SampleSize { get; set; } = 5;
    public List<IndicatorType> EnabledIndicators { get; set; } = new()
    {
        IndicatorType.EmaCrossover,
        IndicatorType.Rsi,
        IndicatorType.SupportResistance,
        IndicatorType.Macd,
        IndicatorType.BollingerBands,
        IndicatorType.CandlePattern,
        IndicatorType.Momentum
    };
}
