namespace Excalibur5.Models;

public enum ChartSnapshotType { TickCandles, Candles }

public sealed class TickSnapshot
{
    public List<decimal> Values { get; init; } = new();
    public List<long> Epochs { get; init; } = new();
    public List<TickDirection> Directions { get; init; } = new();
}

public sealed class CandleSnapshot
{
    public List<CandleData> Candles { get; init; } = new();
    public ChartSnapshotType Type { get; init; }
    public int HighlightIndex { get; init; }
}

public sealed class BalancePoint
{
    public DateTime Time { get; init; }
    public decimal Balance { get; init; }
}

public sealed class DrawdownInfo
{
    public decimal DrawdownValue { get; init; }
    public DateTime OccurredAt { get; init; }
    public decimal PeakBalance { get; init; }
    public decimal TroughBalance { get; init; }
    public TickSnapshot? TickSnapshot { get; init; }
    public CandleSnapshot? CandleSnapshot { get; init; }
}

public sealed class LargestStakeInfo
{
    public TradeHistoryItem Trade { get; init; } = null!;
    public string Market { get; init; } = string.Empty;
    public TickSnapshot? TickSnapshot { get; init; }
    public CandleSnapshot? CandleSnapshot { get; init; }
}

public sealed class LossStreakInfo
{
    public int Length { get; init; }
    public List<decimal> Stakes { get; init; } = new();
    public decimal TotalLost { get; init; }
}

public sealed class StrategyPerformance
{
    public string StrategyName { get; init; } = string.Empty;
    public int TotalTrades { get; init; }
    public int Wins { get; init; }
    public int Losses { get; init; }
    public decimal TotalProfit { get; init; }
    public decimal WinRate => TotalTrades > 0 ? (decimal)Wins / TotalTrades * 100m : 0m;
}
