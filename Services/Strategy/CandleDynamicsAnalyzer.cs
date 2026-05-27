using Excalibur5.Models;
using Excalibur5.Models.Strategy;

namespace Excalibur5.Services.Strategy;

public sealed class CandleDynamicsAnalyzer
{
    private const int MaxCandles = 50;
    private const int MinCandlesForAnalysis = 5;
    private const int ForecastPatternLength = 3;
    private const int MinForecastSamples = 3;
    private const double ForecastProbabilityThreshold = 0.62;
    private const int IndecisionLookback = 3;
    private const double SmallBodyRatio = 0.30;
    private const double MinAverageBodyRatio = 0.35;
    private const int ForceLookback = 3;
    private const double MinForceBodyRatio = 0.48;
    private const double StrongForceThreshold = 0.35;
    private const double RejectionWickRatio = 0.38;

    private readonly List<CandleRecord> _candles = new(MaxCandles);
    private int _currentStreakLength;
    private SignalDirection _currentStreakDirection = SignalDirection.None;
    private double _velocityP75;

    public int CandleCount => _candles.Count;

    public void Reset()
    {
        _candles.Clear();
        _currentStreakLength = 0;
        _currentStreakDirection = SignalDirection.None;
        _velocityP75 = 0;
    }

    public void UpdateWithCandle(CandleData candle, IReadOnlyList<decimal> ticks)
    {
        int ups = 0, downs = 0;
        decimal velocity = 0;
        for (int i = 1; i < ticks.Count; i++)
        {
            if (ticks[i] > ticks[i - 1]) ups++;
            else if (ticks[i] < ticks[i - 1]) downs++;
            velocity += Math.Abs(ticks[i] - ticks[i - 1]);
        }
        if (ticks.Count > 1)
            velocity /= (ticks.Count - 1);

        decimal body = Math.Abs(candle.Close - candle.Open);
        decimal range = candle.High - candle.Low;
        double bodyRatio = range > 0 ? (double)(body / range) : 0;
        var direction = candle.Close > candle.Open ? SignalDirection.Call : SignalDirection.Put;
        double upperWickRatio = GetWickRatio(candle.High - Math.Max(candle.Open, candle.Close), range);
        double lowerWickRatio = GetWickRatio(Math.Min(candle.Open, candle.Close) - candle.Low, range);

        var record = new CandleRecord
        {
            Direction = direction,
            Body = body,
            Range = range,
            BodyRatio = bodyRatio,
            UpperWickRatio = upperWickRatio,
            LowerWickRatio = lowerWickRatio,
            Ups = ups,
            Downs = downs,
            Velocity = velocity,
            FirstHalfMove = ticks.Count >= 5 ? ticks[4] - ticks[0] : 0
        };

        UpdateStreak(direction);

        _candles.Add(record);
        if (_candles.Count > MaxCandles)
            _candles.RemoveAt(0);

        RecalculateVelocityPercentile();
    }

    public IndicatorSignal GetStreakSignal(int minStreak)
    {
        if (_currentStreakLength < minStreak || _currentStreakDirection == SignalDirection.None)
            return new IndicatorSignal { Direction = SignalDirection.None, Strength = 0 };

        if (!HasStreakExhaustion())
            return new IndicatorSignal { Direction = SignalDirection.None, Strength = 0 };

        var reversalDir = _currentStreakDirection == SignalDirection.Call
            ? SignalDirection.Put
            : SignalDirection.Call;

        double strength = _currentStreakLength switch
        {
            3 => 0.35,
            4 => 0.55,
            5 => 0.75,
            _ => Math.Min(0.90, 0.75 + (_currentStreakLength - 5) * 0.05)
        };

        string dirName = _currentStreakDirection == SignalDirection.Call ? "bull" : "bear";
        return new IndicatorSignal
        {
            Direction = reversalDir,
            Strength = Math.Min(0.95, strength + GetRejectionBonus()),
            Reason = $"Exhaustion reversal {_currentStreakLength}x {dirName}",
            Type = IndicatorType.CandleDynamics
        };
    }

    public IndicatorSignal GetCandleForceSignal()
    {
        if (_candles.Count < ForceLookback)
            return NoSignal();

        var force = GetRecentForce();
        if (Math.Abs(force.Score) < StrongForceThreshold)
            return NoSignal();

        var direction = force.Score > 0 ? SignalDirection.Call : SignalDirection.Put;
        var last = _candles[^1];
        if (last.Direction != direction || last.BodyRatio < MinForceBodyRatio)
            return NoSignal();

        double strength = Math.Clamp(Math.Abs(force.Score) * 0.85 + last.BodyRatio * 0.25, 0.35, 0.90);
        string label = direction == SignalDirection.Call ? "BULL" : "BEAR";
        return new IndicatorSignal
        {
            Direction = direction,
            Strength = strength,
            Reason = $"Candle force -> {label} (body={last.BodyRatio:P0}, force={Math.Abs(force.Score):F2})",
            Type = IndicatorType.CandleDynamics
        };
    }

    public bool IsDirectionSupportedByCandleForce(SignalDirection direction)
    {
        if (direction == SignalDirection.None || _candles.Count < ForceLookback)
            return false;

        if (GetExhaustionDirection() == direction)
            return true;

        var force = GetRecentForce();
        double signedForce = direction == SignalDirection.Call ? force.Score : -force.Score;
        var last = _candles[^1];
        return signedForce >= 0.12
            && (last.Direction == direction || last.BodyRatio >= 0.55);
    }

    public IndicatorSignal GetTransitionSignal()
    {
        if (_candles.Count < MinCandlesForAnalysis)
            return new IndicatorSignal { Direction = SignalDirection.None, Strength = 0 };

        var followers = CountFollowersOfLastDirection();
        int toBull = followers.CallCount;
        int toBear = followers.PutCount;
        int total = toBull + toBear;

        if (total < 5)
            return new IndicatorSignal { Direction = SignalDirection.None, Strength = 0 };

        double bullProb = (double)toBull / total;
        double bearProb = (double)toBear / total;

        if (bullProb > 0.55)
        {
            return new IndicatorSignal
            {
                Direction = SignalDirection.Call,
                Strength = (bullProb - 0.5) * 1.2,
                Reason = $"Transition matrix -> BULL ({bullProb:P0})",
                Type = IndicatorType.CandleDynamics
            };
        }

        if (bearProb > 0.55)
        {
            return new IndicatorSignal
            {
                Direction = SignalDirection.Put,
                Strength = (bearProb - 0.5) * 1.2,
                Reason = $"Transition matrix -> BEAR ({bearProb:P0})",
                Type = IndicatorType.CandleDynamics
            };
        }

        return new IndicatorSignal { Direction = SignalDirection.None, Strength = 0 };
    }

    public IndicatorSignal GetPatternForecastSignal()
    {
        if (_candles.Count < ForecastPatternLength + MinForecastSamples)
            return NoSignal();

        var forecast = CountPatternFollowers();
        double totalWeight = forecast.CallWeight + forecast.PutWeight;
        if (forecast.Samples < MinForecastSamples || totalWeight <= 0)
            return NoSignal();

        double callProbability = forecast.CallWeight / totalWeight;
        double putProbability = forecast.PutWeight / totalWeight;
        return BuildForecastSignal(callProbability, putProbability, forecast.Samples);
    }

    public bool IsRecentMarketIndecisive()
    {
        if (_candles.Count < IndecisionLookback)
            return true;

        var recent = _candles.Skip(_candles.Count - IndecisionLookback).ToList();
        double averageBodyRatio = recent.Average(c => c.BodyRatio);
        int smallCandles = recent.Count(c => c.BodyRatio < SmallBodyRatio);

        return smallCandles >= IndecisionLookback - 1
            || averageBodyRatio < MinAverageBodyRatio;
    }

    public bool AreRecentCandlesStrong(int lookback = 3)
    {
        if (_candles.Count < lookback + 1)
            return false;

        int start = _candles.Count - 1 - lookback;
        for (int i = start; i < _candles.Count - 1; i++)
        {
            if (_candles[i].BodyRatio < MinForceBodyRatio)
                return false;
        }

        return true;
    }

    public IndicatorSignal GetVelocitySignal()
    {
        if (_candles.Count < MinCandlesForAnalysis || _velocityP75 <= 0)
            return new IndicatorSignal { Direction = SignalDirection.None, Strength = 0 };

        var last = _candles[^1];
        if ((double)last.Velocity <= _velocityP75)
            return new IndicatorSignal { Direction = SignalDirection.None, Strength = 0 };

        return new IndicatorSignal
        {
            Direction = last.Direction,
            Strength = 0.20,
            Reason = $"High velocity continuation (vel={last.Velocity:F4})",
            Type = IndicatorType.CandleDynamics
        };
    }

    public IndicatorSignal GetInternalTickSignal()
    {
        if (_candles.Count < MinCandlesForAnalysis)
            return new IndicatorSignal { Direction = SignalDirection.None, Strength = 0 };

        var last = _candles[^1];

        if (last.Ups >= 6)
        {
            return new IndicatorSignal
            {
                Direction = SignalDirection.Put,
                Strength = 0.25 + (last.Ups - 6) * 0.08,
                Reason = $"Internal tick reversal ({last.Ups} up-ticks -> bear next)",
                Type = IndicatorType.CandleDynamics
            };
        }

        if (last.Downs >= 6)
        {
            return new IndicatorSignal
            {
                Direction = SignalDirection.Call,
                Strength = 0.25 + (last.Downs - 6) * 0.08,
                Reason = $"Internal tick reversal ({last.Downs} dn-ticks -> bull next)",
                Type = IndicatorType.CandleDynamics
            };
        }

        return new IndicatorSignal { Direction = SignalDirection.None, Strength = 0 };
    }

    public IndicatorSignal GetFirstHalfSignal(IReadOnlyList<decimal> currentTicks)
    {
        if (currentTicks.Count < 5)
            return new IndicatorSignal { Direction = SignalDirection.None, Strength = 0 };

        decimal move = currentTicks[4] - currentTicks[0];
        if (move == 0)
            return new IndicatorSignal { Direction = SignalDirection.None, Strength = 0 };

        var direction = move > 0 ? SignalDirection.Call : SignalDirection.Put;
        double absMove = (double)Math.Abs(move);
        double avgBody = _candles.Count > 0
            ? _candles.Average(c => (double)c.Body)
            : absMove;

        double strength = Math.Clamp(absMove / Math.Max(avgBody, 0.001) * 0.35, 0.15, 0.50);

        return new IndicatorSignal
        {
            Direction = direction,
            Strength = strength,
            Reason = $"First-half momentum ({(move > 0 ? "up" : "dn")} {Math.Abs(move):F4})",
            Type = IndicatorType.CandleDynamics
        };
    }

    private void UpdateStreak(SignalDirection direction)
    {
        if (direction == _currentStreakDirection)
        {
            _currentStreakLength++;
        }
        else
        {
            _currentStreakDirection = direction;
            _currentStreakLength = 1;
        }
    }

    private bool HasStreakExhaustion()
    {
        if (_candles.Count < Math.Max(ForceLookback, _currentStreakLength))
            return false;

        var last = _candles[^1];
        if (HasRejectionAgainstStreak(last))
            return true;

        double averagePreviousBody = GetAveragePreviousStreakBody();
        if (averagePreviousBody <= 0) return false;

        double lastBody = (double)last.Body;
        return lastBody < averagePreviousBody * 0.72
            || last.BodyRatio < 0.42;
    }

    private SignalDirection GetExhaustionDirection()
    {
        if (!HasStreakExhaustion())
            return SignalDirection.None;

        return _currentStreakDirection == SignalDirection.Call
            ? SignalDirection.Put
            : SignalDirection.Call;
    }

    private bool HasRejectionAgainstStreak(CandleRecord candle)
    {
        return _currentStreakDirection switch
        {
            SignalDirection.Call => candle.UpperWickRatio >= RejectionWickRatio,
            SignalDirection.Put => candle.LowerWickRatio >= RejectionWickRatio,
            _ => false
        };
    }

    private double GetAveragePreviousStreakBody()
    {
        int streakStart = Math.Max(0, _candles.Count - _currentStreakLength);
        int end = _candles.Count - 1;
        if (end <= streakStart) return 0;

        return _candles
            .Skip(streakStart)
            .Take(end - streakStart)
            .Average(c => (double)c.Body);
    }

    private double GetRejectionBonus()
    {
        if (_candles.Count == 0) return 0;
        var last = _candles[^1];
        double wick = _currentStreakDirection == SignalDirection.Call
            ? last.UpperWickRatio
            : last.LowerWickRatio;
        return Math.Clamp((wick - RejectionWickRatio) * 0.35, 0, 0.12);
    }

    private (double Score, double Magnitude) GetRecentForce()
    {
        int start = Math.Max(0, _candles.Count - ForceLookback);
        double signed = 0;
        double total = 0;

        for (int i = start; i < _candles.Count; i++)
        {
            double weight = GetForceWeight(_candles[i], i - start);
            signed += _candles[i].Direction == SignalDirection.Call ? weight : -weight;
            total += Math.Abs(weight);
        }

        return total > 0 ? (signed / total, total) : (0, 0);
    }

    private static double GetForceWeight(CandleRecord candle, int offset)
    {
        double recency = 1.0 + offset * 0.25;
        double bodyWeight = Math.Clamp(candle.BodyRatio, 0.05, 1.0);
        double sizeWeight = candle.Range > 0 ? Math.Min((double)candle.Body / Math.Max((double)candle.Range, 0.0001), 1.0) : bodyWeight;
        return recency * (bodyWeight * 0.75 + sizeWeight * 0.25);
    }

    private static double GetWickRatio(decimal wick, decimal range)
    {
        return range > 0 ? Math.Clamp((double)(wick / range), 0, 1) : 0;
    }

    private IndicatorSignal BuildForecastSignal(double callProbability, double putProbability, int samples)
    {
        var direction = callProbability >= putProbability ? SignalDirection.Call : SignalDirection.Put;
        double probability = Math.Max(callProbability, putProbability);
        if (probability < ForecastProbabilityThreshold)
            return NoSignal();

        double strength = 0.35
            + (probability - ForecastProbabilityThreshold) * 1.6
            + Math.Min(samples, 8) * 0.02;

        string label = direction == SignalDirection.Call ? "BULL" : "BEAR";
        return new IndicatorSignal
        {
            Direction = direction,
            Strength = Math.Clamp(strength, 0.35, 0.90),
            Reason = $"Pattern forecast {ForecastPatternLength}c -> {label} ({probability:P0}, n={samples})",
            Type = IndicatorType.CandleDynamics
        };
    }

    private (int CallCount, int PutCount) CountFollowersOfLastDirection()
    {
        int callCount = 0;
        int putCount = 0;
        var lastDirection = _candles[^1].Direction;

        for (int i = 1; i < _candles.Count; i++)
        {
            if (_candles[i - 1].Direction != lastDirection) continue;
            if (_candles[i].Direction == SignalDirection.Call)
                callCount++;
            else if (_candles[i].Direction == SignalDirection.Put)
                putCount++;
        }

        return (callCount, putCount);
    }

    private (double CallWeight, double PutWeight, int Samples) CountPatternFollowers()
    {
        double callWeight = 0;
        double putWeight = 0;
        int samples = 0;
        int currentPatternStart = _candles.Count - ForecastPatternLength;
        int lastCandidateStart = currentPatternStart - ForecastPatternLength;

        for (int start = 0; start < lastCandidateStart; start++)
        {
            if (!MatchesCurrentPattern(start, currentPatternStart)) continue;
            AddFollowerWeight(start, currentPatternStart, ref callWeight, ref putWeight);
            samples++;
        }

        return (callWeight, putWeight, samples);
    }

    private void AddFollowerWeight(int patternStart, int currentPatternStart, ref double callWeight, ref double putWeight)
    {
        double recency = 1.0 + (double)patternStart / Math.Max(1, currentPatternStart) * 0.5;
        var follower = _candles[patternStart + ForecastPatternLength].Direction;
        if (follower == SignalDirection.Call)
            callWeight += recency;
        else if (follower == SignalDirection.Put)
            putWeight += recency;
    }

    private bool MatchesCurrentPattern(int candidateStart, int currentPatternStart)
    {
        for (int offset = 0; offset < ForecastPatternLength; offset++)
        {
            if (_candles[candidateStart + offset].Direction != _candles[currentPatternStart + offset].Direction)
                return false;
        }

        return true;
    }

    private static IndicatorSignal NoSignal()
    {
        return new IndicatorSignal { Direction = SignalDirection.None, Strength = 0 };
    }

    private void RecalculateVelocityPercentile()
    {
        if (_candles.Count < 4) return;

        var velocities = _candles.Select(c => (double)c.Velocity).OrderBy(v => v).ToList();
        int idx = (int)(velocities.Count * 0.75);
        _velocityP75 = velocities[Math.Min(idx, velocities.Count - 1)];
    }

    private sealed class CandleRecord
    {
        public SignalDirection Direction { get; init; }
        public decimal Body { get; init; }
        public decimal Range { get; init; }
        public double BodyRatio { get; init; }
        public double UpperWickRatio { get; init; }
        public double LowerWickRatio { get; init; }
        public int Ups { get; init; }
        public int Downs { get; init; }
        public decimal Velocity { get; init; }
        public decimal FirstHalfMove { get; init; }
    }
}
