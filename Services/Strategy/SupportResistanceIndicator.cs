using Excalibur5.Models;
using Excalibur5.Models.Strategy;

namespace Excalibur5.Services.Strategy;

/// <summary>
/// Support/Resistance indicator.
/// Detects horizontal levels from recent pivot highs/lows.
/// Signal Call when price bounces off support.
/// Signal Put when price bounces off resistance.
/// </summary>
public sealed class SupportResistanceIndicator : IIndicator
{
    private const int LookbackPeriod = 50;
    private const int PivotWindow = 5;
    private const double ProximityPercent = 0.05; // 0.05% proximity to level

    public IndicatorType Type => IndicatorType.SupportResistance;

    public IndicatorSignal Evaluate(IReadOnlyList<CandleData> candles)
    {
        if (candles.Count < LookbackPeriod)
            return new IndicatorSignal { Direction = SignalDirection.None, Strength = 0 };

        int startIdx = candles.Count - LookbackPeriod;
        var supports = new List<decimal>();
        var resistances = new List<decimal>();

        // Find pivot lows (supports) and pivot highs (resistances)
        for (int i = startIdx + PivotWindow; i < candles.Count - PivotWindow; i++)
        {
            if (IsPivotLow(candles, i, PivotWindow))
                supports.Add(candles[i].Low);
            if (IsPivotHigh(candles, i, PivotWindow))
                resistances.Add(candles[i].High);
        }

        if (supports.Count == 0 && resistances.Count == 0)
            return new IndicatorSignal { Direction = SignalDirection.None, Strength = 0 };

        var lastCandle = candles[^1];
        var prevCandle = candles[^2];
        decimal currentPrice = lastCandle.Close;

        // Check bounce off support (price was going down, now bouncing up)
        foreach (var support in supports)
        {
            double proximity = Math.Abs((double)(currentPrice - support) / (double)support) * 100;
            if (proximity < ProximityPercent && lastCandle.Close > lastCandle.Open && prevCandle.Close < prevCandle.Open)
            {
                double strength = Math.Clamp(1.0 - proximity / ProximityPercent, 0.4, 0.9);
                return new IndicatorSignal
                {
                    Direction = SignalDirection.Call,
                    Strength = strength,
                    Reason = $"Rebote no suporte {support:F2} (prox: {proximity:F3}%)"
                };
            }
        }

        // Check bounce off resistance (price was going up, now bouncing down)
        foreach (var resistance in resistances)
        {
            double proximity = Math.Abs((double)(currentPrice - resistance) / (double)resistance) * 100;
            if (proximity < ProximityPercent && lastCandle.Close < lastCandle.Open && prevCandle.Close > prevCandle.Open)
            {
                double strength = Math.Clamp(1.0 - proximity / ProximityPercent, 0.4, 0.9);
                return new IndicatorSignal
                {
                    Direction = SignalDirection.Put,
                    Strength = strength,
                    Reason = $"Rebote na resistência {resistance:F2} (prox: {proximity:F3}%)"
                };
            }
        }

        return new IndicatorSignal { Direction = SignalDirection.None, Strength = 0 };
    }

    public void Reset() { }

    private static bool IsPivotLow(IReadOnlyList<CandleData> candles, int idx, int window)
    {
        decimal low = candles[idx].Low;
        for (int i = idx - window; i <= idx + window; i++)
        {
            if (i == idx) continue;
            if (candles[i].Low <= low) return false;
        }
        return true;
    }

    private static bool IsPivotHigh(IReadOnlyList<CandleData> candles, int idx, int window)
    {
        decimal high = candles[idx].High;
        for (int i = idx - window; i <= idx + window; i++)
        {
            if (i == idx) continue;
            if (candles[i].High >= high) return false;
        }
        return true;
    }
}
