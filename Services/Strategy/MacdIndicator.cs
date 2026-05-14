using Excalibur5.Models;
using Excalibur5.Models.Strategy;

namespace Excalibur5.Services.Strategy;

public sealed class MacdIndicator : IIndicator
{
    private const int FastPeriod = 12;
    private const int SlowPeriod = 26;
    private const int SignalPeriod = 9;

    public IndicatorType Type => IndicatorType.Macd;

    public IndicatorSignal Evaluate(IReadOnlyList<CandleData> candles)
    {
        if (candles.Count < SlowPeriod + SignalPeriod + 2)
            return new IndicatorSignal { Direction = SignalDirection.None, Strength = 0 };

        var closes = new double[candles.Count];
        for (int i = 0; i < candles.Count; i++)
            closes[i] = (double)candles[i].Close;

        var emaFast = CalculateEma(closes, FastPeriod);
        var emaSlow = CalculateEma(closes, SlowPeriod);

        var macdLine = new double[closes.Length];
        for (int i = SlowPeriod - 1; i < closes.Length; i++)
            macdLine[i] = emaFast[i] - emaSlow[i];

        var signalLine = CalculateEmaFrom(macdLine, SignalPeriod, SlowPeriod - 1);

        int last = closes.Length - 1;
        int prev = last - 1;

        double macdCurr = macdLine[last];
        double macdPrev = macdLine[prev];
        double sigCurr = signalLine[last];
        double sigPrev = signalLine[prev];

        double histogram = macdCurr - sigCurr;
        double histPrev = macdPrev - sigPrev;

        bool crossUp = macdPrev <= sigPrev && macdCurr > sigCurr;
        bool crossDown = macdPrev >= sigPrev && macdCurr < sigCurr;

        // Histogram divergence strengthens signal
        bool histGrowing = Math.Abs(histogram) > Math.Abs(histPrev);

        if (crossUp)
        {
            double strength = Math.Clamp(Math.Abs(histogram) * 500 + (histGrowing ? 0.2 : 0), 0.3, 1.0);
            return new IndicatorSignal
            {
                Direction = SignalDirection.Call,
                Strength = strength,
                Reason = $"MACD cruzou signal ↑ (hist: {histogram:F5})"
            };
        }

        if (crossDown)
        {
            double strength = Math.Clamp(Math.Abs(histogram) * 500 + (histGrowing ? 0.2 : 0), 0.3, 1.0);
            return new IndicatorSignal
            {
                Direction = SignalDirection.Put,
                Strength = strength,
                Reason = $"MACD cruzou signal ↓ (hist: {histogram:F5})"
            };
        }

        // Strong histogram momentum without crossover
        if (histogram > 0 && histGrowing && histogram > histPrev * 1.5)
        {
            double strength = Math.Clamp(Math.Abs(histogram) * 300, 0.2, 0.6);
            return new IndicatorSignal
            {
                Direction = SignalDirection.Call,
                Strength = strength,
                Reason = $"MACD momentum ↑ (hist: {histogram:F5})"
            };
        }

        if (histogram < 0 && histGrowing && histogram < histPrev * 1.5)
        {
            double strength = Math.Clamp(Math.Abs(histogram) * 300, 0.2, 0.6);
            return new IndicatorSignal
            {
                Direction = SignalDirection.Put,
                Strength = strength,
                Reason = $"MACD momentum ↓ (hist: {histogram:F5})"
            };
        }

        return new IndicatorSignal { Direction = SignalDirection.None, Strength = 0 };
    }

    public void Reset() { }

    private static double[] CalculateEma(double[] values, int period)
    {
        var result = new double[values.Length];
        double multiplier = 2.0 / (period + 1);

        double sum = 0;
        for (int i = 0; i < period && i < values.Length; i++)
            sum += values[i];
        result[period - 1] = sum / period;

        for (int i = period; i < values.Length; i++)
            result[i] = (values[i] - result[i - 1]) * multiplier + result[i - 1];

        return result;
    }

    private static double[] CalculateEmaFrom(double[] values, int period, int startIdx)
    {
        var result = new double[values.Length];
        double multiplier = 2.0 / (period + 1);

        int smaStart = startIdx;
        int smaEnd = smaStart + period;
        if (smaEnd > values.Length) return result;

        double sum = 0;
        for (int i = smaStart; i < smaEnd; i++)
            sum += values[i];
        result[smaEnd - 1] = sum / period;

        for (int i = smaEnd; i < values.Length; i++)
            result[i] = (values[i] - result[i - 1]) * multiplier + result[i - 1];

        return result;
    }
}
