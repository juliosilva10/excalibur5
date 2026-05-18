using Excalibur5.Models.Strategy;

namespace Excalibur5.Services.Strategy.TickIndicators;

public sealed class TickMomentumIndicator : ITickIndicator
{
    private const int Lookback = 10;

    public IndicatorType Type => IndicatorType.TickMomentum;

    public IndicatorSignal Evaluate(IReadOnlyList<decimal> ticks)
    {
        if (ticks.Count < Lookback + 1)
            return new IndicatorSignal { Direction = SignalDirection.None, Strength = 0 };

        int ups = 0, downs = 0;
        for (int i = ticks.Count - Lookback; i < ticks.Count; i++)
        {
            if (ticks[i] > ticks[i - 1]) ups++;
            else if (ticks[i] < ticks[i - 1]) downs++;
        }

        int total = ups + downs;
        if (total == 0)
            return new IndicatorSignal { Direction = SignalDirection.None, Strength = 0 };

        double ratio = (double)Math.Max(ups, downs) / total;
        double strength = Math.Clamp((ratio - 0.5) * 2.5, 0.1, 0.95);

        if (ups > downs && ups >= 5)
        {
            return new IndicatorSignal
            {
                Direction = SignalDirection.Call,
                Strength = strength,
                Reason = $"Tick momentum ↑ ({ups}/{Lookback})",
                Type = Type
            };
        }

        if (downs > ups && downs >= 5)
        {
            return new IndicatorSignal
            {
                Direction = SignalDirection.Put,
                Strength = strength,
                Reason = $"Tick momentum ↓ ({downs}/{Lookback})",
                Type = Type
            };
        }

        return new IndicatorSignal { Direction = SignalDirection.None, Strength = 0 };
    }

    public void Reset() { }
}
