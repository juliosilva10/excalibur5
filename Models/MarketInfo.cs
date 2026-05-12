namespace Excalibur5.Models;

public sealed class MarketInfo
{
    public string Symbol      { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Category    { get; init; } = string.Empty;
    public int    PipSize     { get; init; } = 2;

    /// <summary>Inner barrier offset at 1-minute duration. Scales with dur^0.513.</summary>
    public decimal BarrierInnerBase { get; init; } = 0.45m;

    /// <summary>Outer barrier offset at 1-minute duration. Scales with dur^0.513.</summary>
    public decimal BarrierOuterBase { get; init; } = 0.86m;

    public static IReadOnlyList<MarketInfo> SyntheticMarkets { get; } = new List<MarketInfo>
    {
        new() { Symbol = "R_10",    DisplayName = "V10",       Category = "Volatility",    PipSize = 3, BarrierInnerBase = 0.450m,  BarrierOuterBase = 0.860m },
        new() { Symbol = "R_25",    DisplayName = "V25",       Category = "Volatility",    PipSize = 4, BarrierInnerBase = 0.6600m, BarrierOuterBase = 1.2520m },
        new() { Symbol = "R_50",    DisplayName = "V50",       Category = "Volatility",    PipSize = 4, BarrierInnerBase = 0.9500m, BarrierOuterBase = 1.8000m },
        new() { Symbol = "R_75",    DisplayName = "V75",       Category = "Volatility",    PipSize = 4, BarrierInnerBase = 1.2000m, BarrierOuterBase = 2.2800m },
        new() { Symbol = "R_100",   DisplayName = "V100",      Category = "Volatility",    PipSize = 2, BarrierInnerBase = 0.40m,   BarrierOuterBase = 0.80m },
        new() { Symbol = "1HZ10V",  DisplayName = "V10 (1s)",  Category = "Volatility 1s", PipSize = 3, BarrierInnerBase = 0.450m,  BarrierOuterBase = 0.860m },
        new() { Symbol = "1HZ25V",  DisplayName = "V25 (1s)",  Category = "Volatility 1s", PipSize = 4, BarrierInnerBase = 0.6600m, BarrierOuterBase = 1.2520m },
        new() { Symbol = "1HZ50V",  DisplayName = "V50 (1s)",  Category = "Volatility 1s", PipSize = 4, BarrierInnerBase = 0.9500m, BarrierOuterBase = 1.8000m },
        new() { Symbol = "1HZ75V",  DisplayName = "V75 (1s)",  Category = "Volatility 1s", PipSize = 4, BarrierInnerBase = 1.2000m, BarrierOuterBase = 2.2800m },
        new() { Symbol = "1HZ100V", DisplayName = "V100 (1s)", Category = "Volatility 1s", PipSize = 2, BarrierInnerBase = 0.40m,   BarrierOuterBase = 0.80m },
    };
}
