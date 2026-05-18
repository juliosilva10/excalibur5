using Excalibur5.Models.Strategy;

namespace Excalibur5.Services.Strategy.TickIndicators;

public sealed class TickRangeIndicator : ITickIndicator
{
    private const int RangeLookback = 50;
    private const double LowThreshold = 0.20;
    private const double HighThreshold = 0.80;

    public IndicatorType Type => IndicatorType.TickRange;

    public IndicatorSignal Evaluate(IReadOnlyList<decimal> ticks)
    {
        if (ticks.Count < RangeLookback)
            return new IndicatorSignal { Direction = SignalDirection.None, Strength = 0 };

        decimal high = decimal.MinValue;
        decimal low = decimal.MaxValue;
        int start = ticks.Count - RangeLookback;

        for (int i = start; i < ticks.Count; i++)
        {
            if (ticks[i] > high) high = ticks[i];
            if (ticks[i] < low) low = ticks[i];
        }

        decimal range = high - low;
        if (range == 0)
            return new IndicatorSignal { Direction = SignalDirection.None, Strength = 0 };

        decimal current = ticks[^1];
        double position = (double)(current - low) / (double)range;

        if (position <= LowThreshold)
        {
            double strength = Math.Clamp((LowThreshold - position) / LowThreshold + 0.4, 0.4, 0.85);
            return new IndicatorSignal
            {
                Direction = SignalDirection.Call,
                Strength = strength,
                Reason = $"Tick range low ({position:P0})",
                Type = Type
            };
        }

        if (position >= HighThreshold)
        {
            double strength = Math.Clamp((position - HighThreshold) / (1.0 - HighThreshold) + 0.4, 0.4, 0.85);
            return new IndicatorSignal
            {
                Direction = SignalDirection.Put,
                Strength = strength,
                Reason = $"Tick range high ({position:P0})",
                Type = Type
            };
        }

        return new IndicatorSignal { Direction = SignalDirection.None, Strength = 0 };
    }

    public void Reset() { }
}
