using Excalibur5.Models;
using Excalibur5.Models.Strategy;

namespace Excalibur5.Services.Strategy;

public sealed class BollingerIndicator : IIndicator
{
    private const int Period = 20;
    private const double StdDevMultiplier = 2.0;

    public IndicatorType Type => IndicatorType.BollingerBands;

    public IndicatorSignal Evaluate(IReadOnlyList<CandleData> candles)
    {
        if (candles.Count < Period + 2)
            return new IndicatorSignal { Direction = SignalDirection.None, Strength = 0 };

        var last = candles[^1];
        var prev = candles[^2];

        double sma = 0;
        for (int i = candles.Count - Period; i < candles.Count; i++)
            sma += (double)candles[i].Close;
        sma /= Period;

        double variance = 0;
        for (int i = candles.Count - Period; i < candles.Count; i++)
        {
            double diff = (double)candles[i].Close - sma;
            variance += diff * diff;
        }
        double stdDev = Math.Sqrt(variance / Period);

        double upperBand = sma + StdDevMultiplier * stdDev;
        double lowerBand = sma - StdDevMultiplier * stdDev;
        double bandwidth = (upperBand - lowerBand) / sma;

        double price = (double)last.Close;
        double prevPrice = (double)prev.Close;

        // Squeeze detection: very narrow bands indicate upcoming breakout
        bool isSqueeze = bandwidth < 0.01;

        // Bounce off lower band (bullish)
        if (price <= lowerBand * 1.001 && last.Close > last.Open && prevPrice < (double)prev.Open)
        {
            double distFromBand = Math.Abs(price - lowerBand) / stdDev;
            double strength = Math.Clamp(1.0 - distFromBand, 0.4, 0.95);
            if (isSqueeze) strength = Math.Min(strength + 0.15, 1.0);
            return new IndicatorSignal
            {
                Direction = SignalDirection.Call,
                Strength = strength,
                Reason = $"BB bounce inferior (bw: {bandwidth:F4})"
            };
        }

        // Bounce off upper band (bearish)
        if (price >= upperBand * 0.999 && last.Close < last.Open && prevPrice > (double)prev.Open)
        {
            double distFromBand = Math.Abs(price - upperBand) / stdDev;
            double strength = Math.Clamp(1.0 - distFromBand, 0.4, 0.95);
            if (isSqueeze) strength = Math.Min(strength + 0.15, 1.0);
            return new IndicatorSignal
            {
                Direction = SignalDirection.Put,
                Strength = strength,
                Reason = $"BB bounce superior (bw: {bandwidth:F4})"
            };
        }

        // Breakout after squeeze
        if (isSqueeze)
        {
            if (price > upperBand && prevPrice <= upperBand)
            {
                return new IndicatorSignal
                {
                    Direction = SignalDirection.Call,
                    Strength = 0.7,
                    Reason = $"BB squeeze breakout ↑ (bw: {bandwidth:F4})"
                };
            }
            if (price < lowerBand && prevPrice >= lowerBand)
            {
                return new IndicatorSignal
                {
                    Direction = SignalDirection.Put,
                    Strength = 0.7,
                    Reason = $"BB squeeze breakout ↓ (bw: {bandwidth:F4})"
                };
            }
        }

        return new IndicatorSignal { Direction = SignalDirection.None, Strength = 0 };
    }

    public void Reset() { }
}
