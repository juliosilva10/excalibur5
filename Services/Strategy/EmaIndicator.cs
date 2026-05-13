using Excalibur5.Models;
using Excalibur5.Models.Strategy;

namespace Excalibur5.Services.Strategy;

/// <summary>
/// EMA 9/21 Crossover indicator.
/// Signal Call when EMA9 crosses above EMA21.
/// Signal Put when EMA9 crosses below EMA21.
/// </summary>
public sealed class EmaIndicator : IIndicator
{
    private const int FastPeriod = 9;
    private const int SlowPeriod = 21;

    public IndicatorType Type => IndicatorType.EmaCrossover;

    public IndicatorSignal Evaluate(IReadOnlyList<CandleData> candles)
    {
        if (candles.Count < SlowPeriod + 2)
            return new IndicatorSignal { Direction = SignalDirection.None, Strength = 0 };

        var closes = new decimal[candles.Count];
        for (int i = 0; i < candles.Count; i++)
            closes[i] = candles[i].Close;

        var emaFast = CalculateEma(closes, FastPeriod);
        var emaSlow = CalculateEma(closes, SlowPeriod);

        int last = emaFast.Length - 1;
        int prev = last - 1;
        if (prev < 0)
            return new IndicatorSignal { Direction = SignalDirection.None, Strength = 0 };

        bool crossUp = emaFast[prev] <= emaSlow[prev] && emaFast[last] > emaSlow[last];
        bool crossDown = emaFast[prev] >= emaSlow[prev] && emaFast[last] < emaSlow[last];

        if (crossUp)
        {
            double gap = (double)(emaFast[last] - emaSlow[last]) / (double)emaSlow[last] * 100;
            double strength = Math.Clamp(gap * 10, 0.3, 1.0);
            return new IndicatorSignal
            {
                Direction = SignalDirection.Call,
                Strength = strength,
                Reason = $"EMA9 cruzou acima da EMA21 (gap: {gap:F4}%)"
            };
        }

        if (crossDown)
        {
            double gap = (double)(emaSlow[last] - emaFast[last]) / (double)emaSlow[last] * 100;
            double strength = Math.Clamp(gap * 10, 0.3, 1.0);
            return new IndicatorSignal
            {
                Direction = SignalDirection.Put,
                Strength = strength,
                Reason = $"EMA9 cruzou abaixo da EMA21 (gap: {gap:F4}%)"
            };
        }

        return new IndicatorSignal { Direction = SignalDirection.None, Strength = 0 };
    }

    public void Reset() { }

    private static decimal[] CalculateEma(decimal[] values, int period)
    {
        var result = new decimal[values.Length];
        decimal multiplier = 2m / (period + 1);

        // SMA for first value
        decimal sum = 0;
        for (int i = 0; i < period && i < values.Length; i++)
            sum += values[i];
        result[period - 1] = sum / period;

        for (int i = period; i < values.Length; i++)
            result[i] = (values[i] - result[i - 1]) * multiplier + result[i - 1];

        return result;
    }
}
