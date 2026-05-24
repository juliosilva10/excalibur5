using Excalibur5.Models;
using Excalibur5.Models.Strategy;

namespace Excalibur5.Services.Strategy.TickIndicators;

public sealed class TickCandleSequenceIndicator
{
    private const int AnalysisWindow = 8;
    private const int MinCandles = 5;
    private const double StrongBodyRatio = 0.55;
    private const int StreakThreshold = 3;
    private const double DominanceRatio = 0.70;
    private const int ForecastPatternLength = 3;
    private const int MinForecastSamples = 3;
    private const double ForecastProbabilityThreshold = 0.62;

    public IndicatorType Type => IndicatorType.TickCandleSequence;

    public IndicatorSignal Evaluate(IList<CandleData> candles)
    {
        if (candles.Count < MinCandles)
            return new IndicatorSignal { Direction = SignalDirection.None, Strength = 0 };

        int window = Math.Min(AnalysisWindow, candles.Count);
        int start = candles.Count - window;

        int bullishCount = 0, bearishCount = 0;
        int bullishStrong = 0, bearishStrong = 0;
        double totalBullishStrength = 0, totalBearishStrength = 0;
        int currentStreak = 0;
        SignalDirection streakDir = SignalDirection.None;
        bool strengthIncreasing = false;

        double prevBodyRatio = 0;
        int increasingCount = 0;
        SignalDirection? lastDir = null;

        for (int i = start; i < candles.Count; i++)
        {
            var candle = candles[i];
            decimal body = Math.Abs(candle.Close - candle.Open);
            decimal totalRange = candle.High - candle.Low;
            if (totalRange == 0) continue;

            double bodyRatio = (double)(body / totalRange);
            bool isBullish = candle.Close > candle.Open;
            var dir = isBullish ? SignalDirection.Call : SignalDirection.Put;

            if (isBullish)
            {
                bullishCount++;
                totalBullishStrength += bodyRatio;
                if (bodyRatio >= StrongBodyRatio) bullishStrong++;
            }
            else
            {
                bearishCount++;
                totalBearishStrength += bodyRatio;
                if (bodyRatio >= StrongBodyRatio) bearishStrong++;
            }

            if (lastDir == dir)
            {
                currentStreak++;
                streakDir = dir;
            }
            else
            {
                currentStreak = 1;
                streakDir = dir;
            }

            if (bodyRatio > prevBodyRatio && prevBodyRatio > 0)
                increasingCount++;

            prevBodyRatio = bodyRatio;
            lastDir = dir;
        }

        int totalCandles = bullishCount + bearishCount;
        if (totalCandles < MinCandles)
            return new IndicatorSignal { Direction = SignalDirection.None, Strength = 0 };

        strengthIncreasing = increasingCount >= (window / 2);

        double bullishDominance = (double)bullishCount / totalCandles;
        double bearishDominance = (double)bearishCount / totalCandles;
        double avgBullishStr = bullishCount > 0 ? totalBullishStrength / bullishCount : 0;
        double avgBearishStr = bearishCount > 0 ? totalBearishStrength / bearishCount : 0;
        var forecastSignal = GetPatternForecastSignal(candles);
        if (forecastSignal.Direction != SignalDirection.None && forecastSignal.Strength >= 0.60)
            return forecastSignal;

        SignalDirection signalDir = SignalDirection.None;
        double confidence = 0;
        string reason = string.Empty;

        if (currentStreak >= StreakThreshold && streakDir != SignalDirection.None)
        {
            if (currentStreak >= 6)
            {
                signalDir = streakDir == SignalDirection.Call ? SignalDirection.Put : SignalDirection.Call;
                confidence = 0.35 + (currentStreak - 6) * 0.08;
                reason = $"Streak exhaustion {currentStreak}x {(streakDir == SignalDirection.Call ? "bull" : "bear")} → reversal likely";
            }
            else
            {
                signalDir = streakDir;
                confidence = 0.5 + (currentStreak - StreakThreshold) * 0.1;
                if (strengthIncreasing) confidence += 0.15;
                reason = $"Streak {currentStreak}x {(streakDir == SignalDirection.Call ? "bull" : "bear")}";
            }
        }
        else if (bullishDominance >= DominanceRatio && bullishStrong >= 2)
        {
            signalDir = SignalDirection.Call;
            confidence = 0.4 + (bullishDominance - DominanceRatio) * 2.0 + avgBullishStr * 0.3;
            reason = $"Candle dominance bull ({bullishCount}/{totalCandles}, strong:{bullishStrong})";
        }
        else if (bearishDominance >= DominanceRatio && bearishStrong >= 2)
        {
            signalDir = SignalDirection.Put;
            confidence = 0.4 + (bearishDominance - DominanceRatio) * 2.0 + avgBearishStr * 0.3;
            reason = $"Candle dominance bear ({bearishCount}/{totalCandles}, strong:{bearishStrong})";
        }
        else if (bullishStrong >= 3 && avgBullishStr > avgBearishStr + 0.15)
        {
            signalDir = SignalDirection.Call;
            confidence = 0.4 + (avgBullishStr - avgBearishStr) * 1.5;
            reason = $"Strong bull candles ({bullishStrong}, avg:{avgBullishStr:F2})";
        }
        else if (bearishStrong >= 3 && avgBearishStr > avgBullishStr + 0.15)
        {
            signalDir = SignalDirection.Put;
            confidence = 0.4 + (avgBearishStr - avgBullishStr) * 1.5;
            reason = $"Strong bear candles ({bearishStrong}, avg:{avgBearishStr:F2})";
        }

        if (signalDir == SignalDirection.None)
            return new IndicatorSignal { Direction = SignalDirection.None, Strength = 0 };

        confidence = Math.Clamp(confidence, 0.3, 0.95);

        return new IndicatorSignal
        {
            Direction = signalDir,
            Strength = confidence,
            Reason = reason,
            Type = Type
        };
    }

    public void Reset() { }

    private static IndicatorSignal GetPatternForecastSignal(IList<CandleData> candles)
    {
        if (candles.Count < ForecastPatternLength + MinForecastSamples)
            return NoSignal();

        var forecast = CountPatternFollowers(candles);
        double totalWeight = forecast.CallWeight + forecast.PutWeight;
        if (forecast.Samples < MinForecastSamples || totalWeight <= 0)
            return NoSignal();

        double callProbability = forecast.CallWeight / totalWeight;
        double putProbability = forecast.PutWeight / totalWeight;
        return BuildForecastSignal(callProbability, putProbability, forecast.Samples);
    }

    private static IndicatorSignal BuildForecastSignal(double callProbability, double putProbability, int samples)
    {
        var direction = callProbability >= putProbability ? SignalDirection.Call : SignalDirection.Put;
        double probability = Math.Max(callProbability, putProbability);
        if (probability < ForecastProbabilityThreshold)
            return NoSignal();

        double strength = 0.38
            + (probability - ForecastProbabilityThreshold) * 1.4
            + Math.Min(samples, 8) * 0.02;

        string label = direction == SignalDirection.Call ? "BULL" : "BEAR";
        return new IndicatorSignal
        {
            Direction = direction,
            Strength = Math.Clamp(strength, 0.38, 0.90),
            Reason = $"Tick candle pattern -> {label} ({probability:P0}, n={samples})",
            Type = IndicatorType.TickCandleSequence
        };
    }

    private static (double CallWeight, double PutWeight, int Samples) CountPatternFollowers(IList<CandleData> candles)
    {
        double callWeight = 0;
        double putWeight = 0;
        int samples = 0;
        int currentPatternStart = candles.Count - ForecastPatternLength;
        int lastCandidateStart = currentPatternStart - ForecastPatternLength;

        for (int start = 0; start < lastCandidateStart; start++)
        {
            if (!MatchesPattern(candles, start, currentPatternStart)) continue;
            AddFollowerWeight(candles, start, currentPatternStart, ref callWeight, ref putWeight);
            samples++;
        }

        return (callWeight, putWeight, samples);
    }

    private static void AddFollowerWeight(IList<CandleData> candles, int start, int currentStart, ref double callWeight, ref double putWeight)
    {
        double recency = 1.0 + (double)start / Math.Max(1, currentStart) * 0.5;
        var direction = GetDirection(candles[start + ForecastPatternLength]);
        if (direction == SignalDirection.Call)
            callWeight += recency;
        else if (direction == SignalDirection.Put)
            putWeight += recency;
    }

    private static bool MatchesPattern(IList<CandleData> candles, int candidateStart, int currentStart)
    {
        for (int offset = 0; offset < ForecastPatternLength; offset++)
        {
            if (GetDirection(candles[candidateStart + offset]) != GetDirection(candles[currentStart + offset]))
                return false;
        }

        return true;
    }

    private static SignalDirection GetDirection(CandleData candle)
    {
        if (candle.Close > candle.Open) return SignalDirection.Call;
        if (candle.Close < candle.Open) return SignalDirection.Put;
        return SignalDirection.None;
    }

    private static IndicatorSignal NoSignal()
    {
        return new IndicatorSignal { Direction = SignalDirection.None, Strength = 0 };
    }
}
