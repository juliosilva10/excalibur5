using Excalibur5.Models;
using Excalibur5.Models.Strategy;

namespace Excalibur5.Services.Strategy;

public sealed class SignalFilter
{
    private const string Src = "SignalFilter";
    private const int TrendEmaPeriod = 50;
    private const int AtrBaselinePeriod = 100;
    private const double AtrDropRatio = 0.3;

    private int _cooldownCandles;

    public bool ShouldFilter(
        IReadOnlyList<CandleData> candles,
        SignalDirection direction,
        double score,
        StrategyConfig config)
    {
        if (candles.Count < TrendEmaPeriod + 2)
            return false;

        if (_cooldownCandles > 0)
        {
            _cooldownCandles--;
            AppLogger.Info(Src, $"Signal filtered: cooldown ({_cooldownCandles} remaining)");
            return true;
        }

        if (candles.Count >= AtrBaselinePeriod && IsVolatilityCollapsed(candles))
            return true;

        if (!IsTrendAligned(candles, direction))
        {
            AppLogger.Info(Src, $"Signal filtered: against EMA50 trend");
            return true;
        }

        return false;
    }

    private static bool IsVolatilityCollapsed(IReadOnlyList<CandleData> candles)
    {
        double atrShort = AtrIndicator.CalculateAtr(candles, 14);
        double atrLong = CalculateAtrFromOffset(candles, 14, AtrBaselinePeriod - 14);

        if (atrLong <= 0) return false;

        double ratio = atrShort / atrLong;
        if (ratio < AtrDropRatio)
        {
            AppLogger.Info(Src, $"Signal filtered: volatility collapsed (ratio: {ratio:F3}, short={atrShort:G4}, long={atrLong:G4})");
            return true;
        }
        return false;
    }

    private static double CalculateAtrFromOffset(IReadOnlyList<CandleData> candles, int period, int offset)
    {
        if (candles.Count < offset + period) return 0;
        var slice = candles.Skip(candles.Count - offset - period).Take(period + 1).ToList();
        return AtrIndicator.CalculateAtr(slice, period);
    }

    public void OnLoss()
    {
        _cooldownCandles = 2;
    }

    public void Reset()
    {
        _cooldownCandles = 0;
    }

    private static bool IsTrendAligned(IReadOnlyList<CandleData> candles, SignalDirection direction)
    {
        double ema50 = CalculateEma50(candles);
        double currentPrice = (double)candles[^1].Close;

        if (direction == SignalDirection.Call)
            return currentPrice >= ema50 * 0.999;

        if (direction == SignalDirection.Put)
            return currentPrice <= ema50 * 1.001;

        return true;
    }

    private static double CalculateEma50(IReadOnlyList<CandleData> candles)
    {
        double multiplier = 2.0 / (TrendEmaPeriod + 1);

        double sum = 0;
        for (int i = 0; i < TrendEmaPeriod && i < candles.Count; i++)
            sum += (double)candles[i].Close;
        double ema = sum / TrendEmaPeriod;

        for (int i = TrendEmaPeriod; i < candles.Count; i++)
            ema = ((double)candles[i].Close - ema) * multiplier + ema;

        return ema;
    }
}
