using Excalibur5.Models;
using Excalibur5.Models.Strategy;

namespace Excalibur5.Services.Strategy;

public sealed class CandleDynamicsAnalyzer
{
    private const int MaxCandles = 50;
    private const int MinCandlesForAnalysis = 5;

    private readonly List<CandleRecord> _candles = new(MaxCandles);
    private readonly int[,] _transitions = new int[2, 2];
    private int _currentStreakLength;
    private SignalDirection _currentStreakDirection = SignalDirection.None;
    private double _velocityP75;

    public int CandleCount => _candles.Count;

    public void Reset()
    {
        _candles.Clear();
        Array.Clear(_transitions);
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

        var record = new CandleRecord
        {
            Direction = direction,
            Body = body,
            Range = range,
            BodyRatio = bodyRatio,
            Ups = ups,
            Downs = downs,
            Velocity = velocity,
            FirstHalfMove = ticks.Count >= 5 ? ticks[4] - ticks[0] : 0
        };

        if (_candles.Count > 0)
        {
            int fromIdx = _candles[^1].Direction == SignalDirection.Call ? 0 : 1;
            int toIdx = direction == SignalDirection.Call ? 0 : 1;
            _transitions[fromIdx, toIdx]++;
        }

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
            Strength = strength,
            Reason = $"Streak reversal {_currentStreakLength}x {dirName}",
            Type = IndicatorType.CandleDynamics
        };
    }

    public IndicatorSignal GetTransitionSignal()
    {
        if (_candles.Count < MinCandlesForAnalysis)
            return new IndicatorSignal { Direction = SignalDirection.None, Strength = 0 };

        int lastIdx = _candles[^1].Direction == SignalDirection.Call ? 0 : 1;
        int toBull = _transitions[lastIdx, 0];
        int toBear = _transitions[lastIdx, 1];
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
        public int Ups { get; init; }
        public int Downs { get; init; }
        public decimal Velocity { get; init; }
        public decimal FirstHalfMove { get; init; }
    }
}
