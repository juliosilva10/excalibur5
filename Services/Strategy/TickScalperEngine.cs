using Excalibur5.Models;
using Excalibur5.Models.Strategy;
using Excalibur5.Services.Strategy.TickIndicators;

namespace Excalibur5.Services.Strategy;

public sealed class TickScalperEngine
{
    private const string Src = "TickScalper";
    private const int MinTicksRequired = 30;
    private const int MaxTicks = 200;
    private const int DirectionLookback = 3;
    private const int ChopLookback = 20;
    private const double ChopThreshold = 0.60;
    private const double MinCandleBodyRatio = 0.40;
    private const int CandleFlowLookback = 5;
    private const int MinExpressiveCandles = 1;

    private readonly List<decimal> _ticks = new(MaxTicks);
    private readonly List<ITickIndicator> _indicators = new();
    private readonly List<CandleData> _tickCandles = new();
    private readonly TickCandleSequenceIndicator _candleSequenceIndicator = new();
    private int _cooldownTicks;
    private int _cooldownSetting = 12;
    private double _threshold = 0.80;
    private int _minAgreement = 3;
    private bool _flatFilter = true;
    private bool _isRunning;
    private int _consecutiveLosses;

    public event EventHandler<TradeSignal>? SignalGenerated;
    public bool IsRunning => _isRunning;

    public void Start(int cooldown, double threshold, int minAgreement, bool flatFilter)
    {
        _cooldownSetting = cooldown;
        _threshold = threshold;
        _minAgreement = minAgreement;
        _flatFilter = flatFilter;
        _cooldownTicks = 0;
        _consecutiveLosses = 0;
        _ticks.Clear();
        _indicators.Clear();

        _indicators.Add(new TickMomentumIndicator());
        _indicators.Add(new TickEmaCrossoverIndicator());
        _indicators.Add(new TickVelocityIndicator());
        _indicators.Add(new TickReversalIndicator());
        _indicators.Add(new TickRangeIndicator());

        _isRunning = true;
        AppLogger.Info(Src, $"Started — cooldown={cooldown}, threshold={threshold:P0}, minAgree={minAgreement}, flat={flatFilter}");
    }

    public void Stop()
    {
        _isRunning = false;
        _ticks.Clear();
        _tickCandles.Clear();
        foreach (var ind in _indicators)
            ind.Reset();
        AppLogger.Info(Src, "Stopped");
    }

    public void FeedTickCandles(IList<CandleData> candles)
    {
        _tickCandles.Clear();
        _tickCandles.AddRange(candles);
    }

    public void FeedTick(decimal price)
    {
        if (!_isRunning) return;

        _ticks.Add(price);
        if (_ticks.Count > MaxTicks)
            _ticks.RemoveAt(0);

        if (_cooldownTicks > 0)
        {
            _cooldownTicks--;
            return;
        }

        if (_ticks.Count < MinTicksRequired) return;

        if (_flatFilter && IsFlat())
            return;

        if (IsChoppy())
            return;

        if (IsCandleTooSmall())
            return;

        EvaluateSignals();
    }

    public void SetCooldown()
    {
        _cooldownTicks = _cooldownSetting;
    }

    public void ReportTradeResult(bool won)
    {
        if (won)
        {
            _consecutiveLosses = 0;
        }
        else
        {
            _consecutiveLosses++;
            int progressiveCooldown = _cooldownSetting * (1 << Math.Min(_consecutiveLosses, 4));
            _cooldownTicks = progressiveCooldown;
            AppLogger.Info(Src, $"Progressive cooldown: {progressiveCooldown} ticks (losses: {_consecutiveLosses})");
        }
    }

    public void FeedHistory(IReadOnlyList<decimal> history)
    {
        _ticks.Clear();
        int start = Math.Max(0, history.Count - MaxTicks);
        for (int i = start; i < history.Count; i++)
            _ticks.Add(history[i]);
    }

    private bool IsFlat()
    {
        int lookback = Math.Min(30, _ticks.Count);
        decimal high = decimal.MinValue;
        decimal low = decimal.MaxValue;

        for (int i = _ticks.Count - lookback; i < _ticks.Count; i++)
        {
            if (_ticks[i] > high) high = _ticks[i];
            if (_ticks[i] < low) low = _ticks[i];
        }

        if (low == 0) return false;
        decimal range = (high - low) / low;
        return range < 0.00005m;
    }

    private bool IsChoppy()
    {
        int lookback = Math.Min(ChopLookback, _ticks.Count - 1);
        if (lookback < 6) return false;

        int reversals = 0;
        int prevDir = 0;

        for (int i = _ticks.Count - lookback; i < _ticks.Count; i++)
        {
            int dir = _ticks[i] > _ticks[i - 1] ? 1 : _ticks[i] < _ticks[i - 1] ? -1 : 0;
            if (dir == 0) continue;
            if (prevDir != 0 && dir != prevDir)
                reversals++;
            prevDir = dir;
        }

        double chopRatio = (double)reversals / (lookback - 1);
        return chopRatio >= ChopThreshold;
    }

    private bool IsCandleTooSmall()
    {
        if (_tickCandles.Count < CandleFlowLookback) return true;

        int expressiveCount = 0;

        int start = Math.Max(0, _tickCandles.Count - CandleFlowLookback);
        for (int i = start; i < _tickCandles.Count; i++)
        {
            var candle = _tickCandles[i];
            decimal body = Math.Abs(candle.Close - candle.Open);
            decimal totalRange = candle.High - candle.Low;

            if (totalRange == 0) continue;

            double bodyRatio = (double)(body / totalRange);
            if (bodyRatio >= MinCandleBodyRatio)
                expressiveCount++;
        }

        if (expressiveCount < MinExpressiveCandles)
        {
            AppLogger.Info(Src, $"Candle flow fraco — apenas {expressiveCount}/{CandleFlowLookback} candles expressivas (min: {MinExpressiveCandles})");
            return true;
        }

        return false;
    }

    private SignalDirection? GetCandleDirection()
    {
        if (_tickCandles.Count < CandleFlowLookback) return null;

        int bullish = 0, bearish = 0;
        int start = Math.Max(0, _tickCandles.Count - CandleFlowLookback);

        for (int i = start; i < _tickCandles.Count; i++)
        {
            var candle = _tickCandles[i];
            decimal body = Math.Abs(candle.Close - candle.Open);
            decimal totalRange = candle.High - candle.Low;
            if (totalRange == 0) continue;

            double bodyRatio = (double)(body / totalRange);
            if (bodyRatio < MinCandleBodyRatio) continue;

            if (candle.Close > candle.Open) bullish++;
            else bearish++;
        }

        if (bullish >= MinExpressiveCandles && bullish > bearish) return SignalDirection.Call;
        if (bearish >= MinExpressiveCandles && bearish > bullish) return SignalDirection.Put;
        return null;
    }

    private bool IsDirectionConfirmed(SignalDirection direction)
    {
        if (_ticks.Count < DirectionLookback + 1) return true;

        int ups = 0, downs = 0;
        for (int i = _ticks.Count - DirectionLookback; i < _ticks.Count; i++)
        {
            if (_ticks[i] > _ticks[i - 1]) ups++;
            else if (_ticks[i] < _ticks[i - 1]) downs++;
        }

        if (direction == SignalDirection.Call)
            return downs < DirectionLookback;
        if (direction == SignalDirection.Put)
            return ups < DirectionLookback;

        return true;
    }

    private void EvaluateSignals()
    {
        var signals = new List<IndicatorSignal>();
        foreach (var indicator in _indicators)
        {
            var signal = indicator.Evaluate(_ticks);
            signals.Add(signal);
        }

        var candleSeqSignal = _candleSequenceIndicator.Evaluate(_tickCandles);

        int callCount = 0, putCount = 0;
        double callScore = 0, putScore = 0;

        foreach (var s in signals)
        {
            if (s.Direction == SignalDirection.Call && s.Strength > 0)
            {
                callCount++;
                callScore += s.Strength;
            }
            else if (s.Direction == SignalDirection.Put && s.Strength > 0)
            {
                putCount++;
                putScore += s.Strength;
            }
        }

        if (candleSeqSignal.Direction == SignalDirection.Call && candleSeqSignal.Strength > 0)
        {
            callCount++;
            callScore += candleSeqSignal.Strength * 1.5;
        }
        else if (candleSeqSignal.Direction == SignalDirection.Put && candleSeqSignal.Strength > 0)
        {
            putCount++;
            putScore += candleSeqSignal.Strength * 1.5;
        }

        SignalDirection direction;
        int count;
        double score;

        if (callCount >= putCount && callCount >= _minAgreement)
        {
            direction = SignalDirection.Call;
            count = callCount;
            score = callScore / count;
        }
        else if (putCount > callCount && putCount >= _minAgreement)
        {
            direction = SignalDirection.Put;
            count = putCount;
            score = putScore / count;
        }
        else
        {
            return;
        }

        if (score < _threshold) return;

        if (!IsDirectionConfirmed(direction))
        {
            AppLogger.Info(Src, $"Signal {direction} rejected — recent ticks contradict direction");
            return;
        }

        if (candleSeqSignal.Direction != SignalDirection.None && candleSeqSignal.Strength >= 0.5 && candleSeqSignal.Direction != direction)
        {
            AppLogger.Info(Src, $"Signal {direction} rejected — candle sequence strongly points {candleSeqSignal.Direction} ({candleSeqSignal.Reason})");
            return;
        }

        var candleDir = GetCandleDirection();
        if (candleDir != null && candleDir != direction)
        {
            AppLogger.Info(Src, $"Signal {direction} rejected — last candle points {candleDir}");
            return;
        }

        var reasons = string.Join(" + ", signals
            .Where(s => s.Direction == direction && s.Strength > 0)
            .Select(s => s.Reason));

        var contributors = signals
            .Where(s => s.Direction == direction && s.Strength > 0)
            .Select(s => s.Type)
            .ToList();

        if (candleSeqSignal.Direction == direction && candleSeqSignal.Strength > 0)
        {
            reasons += $" + {candleSeqSignal.Reason}";
            contributors.Add(candleSeqSignal.Type);
        }

        int totalIndicators = _indicators.Count + 1;
        var tradeSignal = new TradeSignal
        {
            Direction = direction,
            Confidence = score,
            Reason = reasons,
            Timestamp = DateTimeOffset.UtcNow,
            ContributingIndicators = contributors
        };

        AppLogger.Info(Src, $"Signal: {direction} (score: {score:P0}, agree: {count}/{totalIndicators}) — {reasons}");
        SignalGenerated?.Invoke(this, tradeSignal);
    }
}
