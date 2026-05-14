using Excalibur5.Models;
using Excalibur5.Models.Strategy;

namespace Excalibur5.Services.Strategy;

public sealed class CandlePatternIndicator : IIndicator
{
    public IndicatorType Type => IndicatorType.CandlePattern;

    public IndicatorSignal Evaluate(IReadOnlyList<CandleData> candles)
    {
        if (candles.Count < 5)
            return new IndicatorSignal { Direction = SignalDirection.None, Strength = 0 };

        var c0 = candles[^1];
        var c1 = candles[^2];
        var c2 = candles[^3];

        double body0 = (double)(c0.Close - c0.Open);
        double body1 = (double)(c1.Close - c1.Open);
        double absBody0 = Math.Abs(body0);
        double absBody1 = Math.Abs(body1);
        double range0 = (double)(c0.High - c0.Low);
        double range1 = (double)(c1.High - c1.Low);

        if (range0 == 0) range0 = 0.00001;
        if (range1 == 0) range1 = 0.00001;

        // Bullish Engulfing
        if (body1 < 0 && body0 > 0 && c0.Open <= c1.Close && c0.Close >= c1.Open && absBody0 > absBody1)
        {
            double strength = Math.Clamp(absBody0 / range0, 0.5, 0.95);
            return new IndicatorSignal
            {
                Direction = SignalDirection.Call,
                Strength = strength,
                Reason = "Bullish Engulfing"
            };
        }

        // Bearish Engulfing
        if (body1 > 0 && body0 < 0 && c0.Open >= c1.Close && c0.Close <= c1.Open && absBody0 > absBody1)
        {
            double strength = Math.Clamp(absBody0 / range0, 0.5, 0.95);
            return new IndicatorSignal
            {
                Direction = SignalDirection.Put,
                Strength = strength,
                Reason = "Bearish Engulfing"
            };
        }

        // Hammer (bullish reversal at bottom)
        double lowerShadow0 = (double)(Math.Min(c0.Open, c0.Close) - c0.Low);
        double upperShadow0 = (double)(c0.High - Math.Max(c0.Open, c0.Close));
        if (lowerShadow0 > absBody0 * 2 && upperShadow0 < absBody0 * 0.5 && body1 < 0)
        {
            double strength = Math.Clamp(lowerShadow0 / range0, 0.4, 0.85);
            return new IndicatorSignal
            {
                Direction = SignalDirection.Call,
                Strength = strength,
                Reason = "Hammer"
            };
        }

        // Shooting Star (bearish reversal at top)
        if (upperShadow0 > absBody0 * 2 && lowerShadow0 < absBody0 * 0.5 && body1 > 0)
        {
            double strength = Math.Clamp(upperShadow0 / range0, 0.4, 0.85);
            return new IndicatorSignal
            {
                Direction = SignalDirection.Put,
                Strength = strength,
                Reason = "Shooting Star"
            };
        }

        // Morning Star (3-candle bullish reversal)
        double body2 = (double)(c2.Close - c2.Open);
        double absBody2 = Math.Abs(body2);
        double range2 = (double)(c2.High - c2.Low);
        if (range2 == 0) range2 = 0.00001;

        if (body2 < 0 && absBody1 < absBody2 * 0.3 && body0 > 0 && (double)c0.Close > (double)c2.Open + absBody2 * 0.5)
        {
            return new IndicatorSignal
            {
                Direction = SignalDirection.Call,
                Strength = 0.8,
                Reason = "Morning Star"
            };
        }

        // Evening Star (3-candle bearish reversal)
        if (body2 > 0 && absBody1 < absBody2 * 0.3 && body0 < 0 && (double)c0.Close < (double)c2.Open - absBody2 * 0.5)
        {
            return new IndicatorSignal
            {
                Direction = SignalDirection.Put,
                Strength = 0.8,
                Reason = "Evening Star"
            };
        }

        // Three White Soldiers
        if (candles.Count >= 4)
        {
            var c3 = candles[^4];
            double body3 = (double)(c3.Close - c3.Open);
            if (body2 > 0 && body1 > 0 && body0 > 0 && body3 <= 0 &&
                c1.Open > c2.Open && c0.Open > c1.Open &&
                c1.Close > c2.Close && c0.Close > c1.Close)
            {
                return new IndicatorSignal
                {
                    Direction = SignalDirection.Call,
                    Strength = 0.85,
                    Reason = "Three White Soldiers"
                };
            }

            // Three Black Crows
            if (body2 < 0 && body1 < 0 && body0 < 0 && body3 >= 0 &&
                c1.Open < c2.Open && c0.Open < c1.Open &&
                c1.Close < c2.Close && c0.Close < c1.Close)
            {
                return new IndicatorSignal
                {
                    Direction = SignalDirection.Put,
                    Strength = 0.85,
                    Reason = "Three Black Crows"
                };
            }
        }

        // Doji (indecision — reduce confidence, return None)
        if (absBody0 < range0 * 0.1 && range0 > 0)
        {
            return new IndicatorSignal
            {
                Direction = SignalDirection.None,
                Strength = -0.3,
                Reason = "Doji (indecisão)"
            };
        }

        return new IndicatorSignal { Direction = SignalDirection.None, Strength = 0 };
    }

    public void Reset() { }
}
