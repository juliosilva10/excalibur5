using Excalibur5.Models;
using Excalibur5.Models.Strategy;

namespace Excalibur5.Services.Strategy;

public sealed class AtrIndicator : IIndicator
{
    private const int Period = 14;
    private const double LowVolThreshold = 0.0003;

    public IndicatorType Type => IndicatorType.Atr;
    public double CurrentAtr { get; private set; }

    public IndicatorSignal Evaluate(IReadOnlyList<CandleData> candles)
    {
        if (candles.Count < Period + 2)
        {
            CurrentAtr = 0;
            return new IndicatorSignal { Direction = SignalDirection.None, Strength = 0 };
        }

        double atr = CalculateAtr(candles, Period);
        CurrentAtr = atr;

        double priceLevel = (double)candles[^1].Close;
        double normalizedAtr = atr / priceLevel;

        // ATR is a filter, not a directional indicator.
        // Return None direction but store the ATR value for SignalFilter to use.
        // However, extreme ATR spikes can indicate reversal opportunities.

        double prevAtr = CalculateAtr(candles.Take(candles.Count - 1).ToList(), Period);
        double atrChange = (atr - prevAtr) / prevAtr;

        // Sudden volatility spike after calm period — potential reversal
        if (atrChange > 0.5 && normalizedAtr > LowVolThreshold * 3)
        {
            var last = candles[^1];
            var direction = last.Close > last.Open ? SignalDirection.Call : SignalDirection.Put;
            return new IndicatorSignal
            {
                Direction = direction,
                Strength = Math.Clamp(atrChange * 0.5, 0.2, 0.5),
                Reason = $"ATR spike ({normalizedAtr:F5}, +{atrChange:P0})"
            };
        }

        return new IndicatorSignal { Direction = SignalDirection.None, Strength = 0 };
    }

    public void Reset() => CurrentAtr = 0;

    public static double CalculateAtr(IReadOnlyList<CandleData> candles, int period)
    {
        if (candles.Count < period + 1) return 0;

        double atr = 0;
        int start = candles.Count - period - 1;

        for (int i = start + 1; i <= start + period; i++)
        {
            double tr = TrueRange(candles[i], candles[i - 1]);
            atr += tr;
        }
        atr /= period;

        for (int i = start + period + 1; i < candles.Count; i++)
        {
            double tr = TrueRange(candles[i], candles[i - 1]);
            atr = (atr * (period - 1) + tr) / period;
        }

        return atr;
    }

    private static double TrueRange(CandleData current, CandleData previous)
    {
        double hl = (double)(current.High - current.Low);
        double hc = Math.Abs((double)(current.High - previous.Close));
        double lc = Math.Abs((double)(current.Low - previous.Close));
        return Math.Max(hl, Math.Max(hc, lc));
    }
}
