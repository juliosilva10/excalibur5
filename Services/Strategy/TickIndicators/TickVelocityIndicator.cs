using Excalibur5.Models.Strategy;

namespace Excalibur5.Services.Strategy.TickIndicators;

public sealed class TickVelocityIndicator : ITickIndicator
{
    private const int ShortWindow = 5;
    private const int LongWindow = 20;

    public IndicatorType Type => IndicatorType.TickVelocity;

    public IndicatorSignal Evaluate(IReadOnlyList<decimal> ticks)
    {
        if (ticks.Count < LongWindow + 1)
            return new IndicatorSignal { Direction = SignalDirection.None, Strength = 0 };

        double shortVelocity = AverageVelocity(ticks, ShortWindow);
        double longVelocity = AverageVelocity(ticks, LongWindow);

        double acceleration = shortVelocity - longVelocity;
        double absAccel = Math.Abs(acceleration);

        if (absAccel < 0.00001)
            return new IndicatorSignal { Direction = SignalDirection.None, Strength = 0 };

        double strength = Math.Clamp(absAccel * 2000, 0.2, 0.9);

        if (acceleration > 0 && shortVelocity > 0)
        {
            return new IndicatorSignal
            {
                Direction = SignalDirection.Call,
                Strength = strength,
                Reason = $"Tick accel ↑ ({shortVelocity:F6}/t)",
                Type = Type
            };
        }

        if (acceleration < 0 && shortVelocity < 0)
        {
            return new IndicatorSignal
            {
                Direction = SignalDirection.Put,
                Strength = strength,
                Reason = $"Tick accel ↓ ({shortVelocity:F6}/t)",
                Type = Type
            };
        }

        return new IndicatorSignal { Direction = SignalDirection.None, Strength = 0 };
    }

    public void Reset() { }

    private static double AverageVelocity(IReadOnlyList<decimal> ticks, int window)
    {
        double sum = 0;
        int start = ticks.Count - window;
        for (int i = start; i < ticks.Count; i++)
            sum += (double)(ticks[i] - ticks[i - 1]);
        return sum / window;
    }
}
