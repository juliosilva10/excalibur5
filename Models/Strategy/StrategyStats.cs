namespace Excalibur5.Models.Strategy;

public sealed class StrategyStats
{
    public int TotalTrades { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public decimal AccumulatedProfit { get; set; }

    public double WinRate => TotalTrades > 0 ? (double)Wins / TotalTrades : 0;

    public void RecordWin(decimal profit)
    {
        TotalTrades++;
        Wins++;
        AccumulatedProfit += profit;
    }

    public void RecordLoss(decimal loss)
    {
        TotalTrades++;
        Losses++;
        AccumulatedProfit += loss; // loss is negative
    }

    public void Reset()
    {
        TotalTrades = 0;
        Wins = 0;
        Losses = 0;
        AccumulatedProfit = 0;
    }
}
