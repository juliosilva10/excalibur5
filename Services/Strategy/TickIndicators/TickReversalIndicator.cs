using Excalibur5.Models.Strategy;

namespace Excalibur5.Services.Strategy.TickIndicators;

public sealed class TickReversalIndicator : ITickIndicator
{
    private const int TrendLength = 5;
    private const int ReversalLength = 3;

    public IndicatorType Type => IndicatorType.TickReversal;

    public IndicatorSignal Evaluate(IReadOnlyList<decimal> ticks)
    {
        if (ticks.Count < TrendLength + ReversalLength + 2)
            return new IndicatorSignal { Direction = SignalDirection.None, Strength = 0 };

        int reversalStart = ticks.Count - ReversalLength;
        int trendStart = reversalStart - TrendLength;

        int trendUps = 0, trendDowns = 0;
        for (int i = trendStart; i < reversalStart; i++)
        {
            if (ticks[i + 1] > ticks[i]) trendUps++;
            else if (ticks[i + 1] < ticks[i]) trendDowns++;
        }

        int revUps = 0, revDowns = 0;
        for (int i = reversalStart; i < ticks.Count - 1; i++)
        {
            if (ticks[i + 1] > ticks[i]) revUps++;
            else if (ticks[i + 1] < ticks[i]) revDowns++;
        }

        bool wasFalling = trendDowns >= TrendLength - 1;
        bool wasRising = trendUps >= TrendLength - 1;
        bool reversedUp = revUps >= ReversalLength - 1;
        bool reversedDown = revDowns >= ReversalLength - 1;

        if (wasFalling && reversedUp)
        {
            double trendStrength = (double)trendDowns / TrendLength;
            double strength = Math.Clamp(trendStrength * 0.8, 0.4, 0.85);
            return new IndicatorSignal
            {
                Direction = SignalDirection.Call,
                Strength = strength,
                Reason = $"Tick reversal ↑ (trend {trendDowns}↓ → {revUps}↑)",
                Type = Type
            };
        }

        if (wasRising && reversedDown)
        {
            double trendStrength = (double)trendUps / TrendLength;
            double strength = Math.Clamp(trendStrength * 0.8, 0.4, 0.85);
            return new IndicatorSignal
            {
                Direction = SignalDirection.Put,
                Strength = strength,
                Reason = $"Tick reversal ↓ (trend {trendUps}↑ → {revDowns}↓)",
                Type = Type
            };
        }

        return new IndicatorSignal { Direction = SignalDirection.None, Strength = 0 };
    }

    public void Reset() { }
}
