using Excalibur5.Models.Strategy;

namespace Excalibur5.Services.Strategy.TickIndicators;

public sealed class TickEmaCrossoverIndicator : ITickIndicator
{
    private const int FastPeriod = 5;
    private const int SlowPeriod = 20;

    public IndicatorType Type => IndicatorType.TickEmaCrossover;

    public IndicatorSignal Evaluate(IReadOnlyList<decimal> ticks)
    {
        if (ticks.Count < SlowPeriod + 2)
            return new IndicatorSignal { Direction = SignalDirection.None, Strength = 0 };

        double fastEma = CalculateEma(ticks, FastPeriod);
        double slowEma = CalculateEma(ticks, SlowPeriod);

        double prevFastEma = CalculateEma(ticks, FastPeriod, 1);
        double prevSlowEma = CalculateEma(ticks, SlowPeriod, 1);

        bool crossUp = prevFastEma <= prevSlowEma && fastEma > slowEma;
        bool crossDown = prevFastEma >= prevSlowEma && fastEma < slowEma;

        double gap = Math.Abs(fastEma - slowEma) / slowEma;
        double strength = Math.Clamp(gap * 500, 0.3, 0.95);

        if (crossUp || (fastEma > slowEma && fastEma > prevFastEma))
        {
            if (crossUp) strength = Math.Min(strength + 0.2, 0.95);
            return new IndicatorSignal
            {
                Direction = SignalDirection.Call,
                Strength = strength,
                Reason = crossUp ? $"Tick EMA cross ↑" : $"Tick EMA bullish",
                Type = Type
            };
        }

        if (crossDown || (fastEma < slowEma && fastEma < prevFastEma))
        {
            if (crossDown) strength = Math.Min(strength + 0.2, 0.95);
            return new IndicatorSignal
            {
                Direction = SignalDirection.Put,
                Strength = strength,
                Reason = crossDown ? $"Tick EMA cross ↓" : $"Tick EMA bearish",
                Type = Type
            };
        }

        return new IndicatorSignal { Direction = SignalDirection.None, Strength = 0 };
    }

    public void Reset() { }

    private static double CalculateEma(IReadOnlyList<decimal> ticks, int period, int offset = 0)
    {
        int end = ticks.Count - offset;
        if (end < period) return (double)ticks[end - 1];

        double multiplier = 2.0 / (period + 1);
        double sum = 0;
        int start = end - period;
        for (int i = start; i < start + period && i < end; i++)
            sum += (double)ticks[i];
        double ema = sum / period;

        for (int i = start + period; i < end; i++)
            ema = ((double)ticks[i] - ema) * multiplier + ema;

        return ema;
    }
}
