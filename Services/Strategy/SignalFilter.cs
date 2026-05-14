using Excalibur5.Models;
using Excalibur5.Models.Strategy;

namespace Excalibur5.Services.Strategy;

public sealed class SignalFilter
{
    private const string Src = "SignalFilter";
    private const double MinAtrThreshold = 0.0003;
    private const int TrendEmaPeriod = 50;

    private int _cooldownCandles;

    public bool ShouldFilter(
        IReadOnlyList<CandleData> candles,
        SignalDirection direction,
        double score,
        StrategyConfig config)
    {
        if (candles.Count < TrendEmaPeriod + 2)
            return false;

        // Cooldown after loss
        if (_cooldownCandles > 0)
        {
            _cooldownCandles--;
            AppLogger.Info(Src, $"Signal filtered: cooldown ({_cooldownCandles} remaining)");
            return true;
        }

        // ATR filter: skip low volatility
        double atr = AtrIndicator.CalculateAtr(candles, 14);
        double price = (double)candles[^1].Close;
        double normalizedAtr = atr / price;

        if (normalizedAtr < MinAtrThreshold)
        {
            AppLogger.Info(Src, $"Signal filtered: low volatility (ATR: {normalizedAtr:F6})");
            return true;
        }

        // Trend alignment: EMA 50
        if (!IsTrendAligned(candles, direction))
        {
            AppLogger.Info(Src, $"Signal filtered: against EMA50 trend");
            return true;
        }

        return false;
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
