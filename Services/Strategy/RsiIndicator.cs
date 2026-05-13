using Excalibur5.Models;
using Excalibur5.Models.Strategy;

namespace Excalibur5.Services.Strategy;

/// <summary>
/// RSI (Relative Strength Index) with period 14.
/// Signal Call when RSI &lt; 30 (oversold).
/// Signal Put when RSI &gt; 70 (overbought).
/// </summary>
public sealed class RsiIndicator : IIndicator
{
    private const int Period = 14;

    public IndicatorType Type => IndicatorType.Rsi;

    public IndicatorSignal Evaluate(IReadOnlyList<CandleData> candles)
    {
        if (candles.Count < Period + 2)
            return new IndicatorSignal { Direction = SignalDirection.None, Strength = 0 };

        double rsi = CalculateRsi(candles, Period);

        if (rsi < 30)
        {
            double strength = Math.Clamp((30 - rsi) / 30, 0.3, 1.0);
            return new IndicatorSignal
            {
                Direction = SignalDirection.Call,
                Strength = strength,
                Reason = $"RSI em sobrevenda ({rsi:F1})"
            };
        }

        if (rsi > 70)
        {
            double strength = Math.Clamp((rsi - 70) / 30, 0.3, 1.0);
            return new IndicatorSignal
            {
                Direction = SignalDirection.Put,
                Strength = strength,
                Reason = $"RSI em sobrecompra ({rsi:F1})"
            };
        }

        return new IndicatorSignal { Direction = SignalDirection.None, Strength = 0 };
    }

    public void Reset() { }

    private static double CalculateRsi(IReadOnlyList<CandleData> candles, int period)
    {
        double avgGain = 0, avgLoss = 0;

        for (int i = 1; i <= period; i++)
        {
            double change = (double)(candles[i].Close - candles[i - 1].Close);
            if (change > 0) avgGain += change;
            else avgLoss += Math.Abs(change);
        }

        avgGain /= period;
        avgLoss /= period;

        for (int i = period + 1; i < candles.Count; i++)
        {
            double change = (double)(candles[i].Close - candles[i - 1].Close);
            if (change > 0)
            {
                avgGain = (avgGain * (period - 1) + change) / period;
                avgLoss = (avgLoss * (period - 1)) / period;
            }
            else
            {
                avgGain = (avgGain * (period - 1)) / period;
                avgLoss = (avgLoss * (period - 1) + Math.Abs(change)) / period;
            }
        }

        if (avgLoss == 0) return 100;
        double rs = avgGain / avgLoss;
        return 100 - (100 / (1 + rs));
    }
}
