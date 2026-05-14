using Excalibur5.Models;
using Excalibur5.Models.Strategy;

namespace Excalibur5.Services.Strategy;

public sealed class MomentumIndicator : IIndicator
{
    private const int RocPeriod = 10;
    private const int SmaPeriod = 3;

    public IndicatorType Type => IndicatorType.Momentum;

    public IndicatorSignal Evaluate(IReadOnlyList<CandleData> candles)
    {
        if (candles.Count < RocPeriod + SmaPeriod + 2)
            return new IndicatorSignal { Direction = SignalDirection.None, Strength = 0 };

        int count = candles.Count;
        double currentRoc = Roc(candles, count - 1);
        double prevRoc = Roc(candles, count - 2);

        double rocSma = 0;
        for (int i = 0; i < SmaPeriod; i++)
            rocSma += Roc(candles, count - 1 - i);
        rocSma /= SmaPeriod;

        double prevRocSma = 0;
        for (int i = 0; i < SmaPeriod; i++)
            prevRocSma += Roc(candles, count - 2 - i);
        prevRocSma /= SmaPeriod;

        // ROC crossing above zero with acceleration
        bool crossUp = prevRocSma <= 0 && rocSma > 0;
        bool crossDown = prevRocSma >= 0 && rocSma < 0;

        if (crossUp)
        {
            double strength = Math.Clamp(Math.Abs(rocSma) * 50, 0.3, 0.9);
            return new IndicatorSignal
            {
                Direction = SignalDirection.Call,
                Strength = strength,
                Reason = $"Momentum ↑ (ROC: {currentRoc:F4}%)"
            };
        }

        if (crossDown)
        {
            double strength = Math.Clamp(Math.Abs(rocSma) * 50, 0.3, 0.9);
            return new IndicatorSignal
            {
                Direction = SignalDirection.Put,
                Strength = strength,
                Reason = $"Momentum ↓ (ROC: {currentRoc:F4}%)"
            };
        }

        // Strong momentum continuation
        if (currentRoc > 0 && currentRoc > prevRoc && rocSma > 0.002)
        {
            double strength = Math.Clamp(rocSma * 30, 0.2, 0.6);
            return new IndicatorSignal
            {
                Direction = SignalDirection.Call,
                Strength = strength,
                Reason = $"Momentum acelerando ↑ (ROC: {currentRoc:F4}%)"
            };
        }

        if (currentRoc < 0 && currentRoc < prevRoc && rocSma < -0.002)
        {
            double strength = Math.Clamp(Math.Abs(rocSma) * 30, 0.2, 0.6);
            return new IndicatorSignal
            {
                Direction = SignalDirection.Put,
                Strength = strength,
                Reason = $"Momentum acelerando ↓ (ROC: {currentRoc:F4}%)"
            };
        }

        return new IndicatorSignal { Direction = SignalDirection.None, Strength = 0 };
    }

    public void Reset() { }

    private static double Roc(IReadOnlyList<CandleData> candles, int idx)
    {
        if (idx < RocPeriod) return 0;
        double current = (double)candles[idx].Close;
        double past = (double)candles[idx - RocPeriod].Close;
        if (past == 0) return 0;
        return (current - past) / past;
    }
}
