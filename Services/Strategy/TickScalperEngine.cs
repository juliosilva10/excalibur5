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
    private const double SmallCandleBodyRatio = 0.30;
    private const int CandleFlowLookback = 5;
    private const int MinExpressiveCandles = 2;
    private const int RecentIndecisionLookback = 3;

    private readonly List<decimal> _ticks = new(MaxTicks);
    private readonly List<ITickIndicator> _indicators = new();
    private readonly List<CandleData> _tickCandles = new();
    private readonly List<decimal> _currentCandleTicks = new();
    private TickCandleManager _candleManager = null!;
    private readonly TickCandleSequenceIndicator _candleSequenceIndicator = new();
    private int _cooldownTicks;
    private int _cooldownSetting = 12;
    private double _threshold = 0.80;
    private int _minAgreement = 3;
    private bool _flatFilter = true;
    private bool _isRunning;
    private int _consecutiveLosses;
    private int _ticksPerCandle = 5;

    public event EventHandler<TradeSignal>? SignalGenerated;
    public bool IsRunning => _isRunning;

    public void Start(int ticksPerCandle, int cooldown, double threshold, int minAgreement, bool flatFilter)
    {
        _ticksPerCandle = ticksPerCandle;
        _cooldownSetting = cooldown;
        _threshold = threshold;
        _minAgreement = minAgreement;
        _flatFilter = flatFilter;
        _cooldownTicks = 0;
        _consecutiveLosses = 0;
        _ticks.Clear();
        _tickCandles.Clear();
        _currentCandleTicks.Clear();
        _indicators.Clear();

        if (_candleManager != null)
            _candleManager.CandleDied -= OnCandleDied;

        _candleManager = new TickCandleManager(ticksPerCandle);
        _candleManager.CandleDied += OnCandleDied;

        _indicators.Add(new TickMomentumIndicator());
        _indicators.Add(new TickEmaCrossoverIndicator());
        _indicators.Add(new TickVelocityIndicator());
        _indicators.Add(new TickReversalIndicator());
        _indicators.Add(new TickRangeIndicator());

        _isRunning = true;
        AppLogger.Info(Src, $"Started — ticksPerCandle={ticksPerCandle}, cooldown={cooldown}, threshold={threshold:P0}, minAgree={minAgreement}, flat={flatFilter}");
    }

    public void Stop()
    {
        _isRunning = false;
        _ticks.Clear();
        _tickCandles.Clear();
        _currentCandleTicks.Clear();
        if (_candleManager != null)
            _candleManager.CandleDied -= OnCandleDied;
        AppLogger.Info(Src, "Stopped");
    }

    public void FeedTick(decimal price)
    {
        if (!_isRunning) return;

        _ticks.Add(price);
        if (_ticks.Count > MaxTicks)
            _ticks.RemoveAt(0);

        _currentCandleTicks.Add(price);
        bool candleDied = _candleManager.FeedTick(price, DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        // Only evaluate signals when a candle dies (completes)
        if (candleDied)
        {
            AppLogger.Info(Src, $"Candle died — evaluating");
            EvaluateSignals();
        }
    }

    private void OnCandleDied(object? sender, CandleDiedEventArgs e)
    {
        _tickCandles.Add(new CandleData
        {
            Epoch = e.BirthEpoch,
            Open = e.Open,
            High = e.High,
            Low = e.Low,
            Close = e.Close
        });

        if (_tickCandles.Count > 100)
            _tickCandles.RemoveAt(0);

        _currentCandleTicks.Clear();
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
            AppLogger.Info(Src, $"Progressive cooldown: {progressiveCooldown} candles (losses: {_consecutiveLosses})");
        }
    }

    public void FeedHistory(IReadOnlyList<decimal> history)
    {
        _ticks.Clear();
        _tickCandles.Clear();
        _currentCandleTicks.Clear();

        int start = Math.Max(0, history.Count - MaxTicks);
        for (int i = start; i < history.Count; i++)
            _ticks.Add(history[i]);

        var candles = _candleManager.FeedHistory(history, null);
        _tickCandles.AddRange(candles.TakeLast(100));
        RestoreCurrentCandleTicks(history, candles.Count);

        AppLogger.Info(Src, $"History fed: {history.Count} ticks");
    }

    private void RestoreCurrentCandleTicks(IReadOnlyList<decimal> history, int completedCandles)
    {
        int firstOpenTick = completedCandles * _ticksPerCandle;
        for (int i = firstOpenTick; i < history.Count; i++)
            _currentCandleTicks.Add(history[i]);
    }

    private bool IsSignalContextBlocked()
    {
        if (_ticks.Count < MinTicksRequired) return true;
        if (_flatFilter && IsFlat()) return true;
        if (IsChoppy()) return true;
        return IsCandleFlowWeak();
    }

    private bool IsFlat()
    {
        int lookback = Math.Min(30, _ticks.Count);
        decimal high = _ticks[^lookback];
        decimal low = high;

        for (int i = _ticks.Count - lookback + 1; i < _ticks.Count; i++)
        {
            high = Math.Max(high, _ticks[i]);
            low = Math.Min(low, _ticks[i]);
        }

        if (low == 0) return false;
        return (high - low) / low < 0.00005m;
    }

    private bool IsChoppy()
    {
        int lookback = Math.Min(ChopLookback, _ticks.Count - 1);
        if (lookback < 6) return false;

        int reversals = CountRecentReversals(lookback);
        double chopRatio = (double)reversals / (lookback - 1);
        if (chopRatio < ChopThreshold) return false;

        AppLogger.Info(Src, $"Signal filtered: choppy market ({chopRatio:P0} reversals)");
        return true;
    }

    private int CountRecentReversals(int lookback)
    {
        int reversals = 0;
        int previousDirection = 0;
        for (int i = _ticks.Count - lookback; i < _ticks.Count; i++)
        {
            int direction = Math.Sign(_ticks[i] - _ticks[i - 1]);
            if (direction == 0) continue;
            if (previousDirection != 0 && direction != previousDirection)
                reversals++;
            previousDirection = direction;
        }
        return reversals;
    }

    private bool IsCandleFlowWeak()
    {
        if (_tickCandles.Count < CandleFlowLookback) return true;
        if (HasRecentSmallCandles()) return true;

        int expressive = CountExpressiveCandles();
        if (expressive >= MinExpressiveCandles) return false;

        AppLogger.Info(Src, $"Signal filtered: weak candle flow ({expressive}/{CandleFlowLookback})");
        return true;
    }

    private bool HasRecentSmallCandles()
    {
        int start = _tickCandles.Count - RecentIndecisionLookback;
        if (start < 0) return true;

        int smallCount = 0;
        double bodyRatioSum = 0;
        for (int i = start; i < _tickCandles.Count; i++)
        {
            double bodyRatio = GetBodyRatio(_tickCandles[i]);
            bodyRatioSum += bodyRatio;
            if (bodyRatio < SmallCandleBodyRatio)
                smallCount++;
        }

        bool indecisive = smallCount >= RecentIndecisionLookback - 1
            || bodyRatioSum / RecentIndecisionLookback < MinCandleBodyRatio;

        if (indecisive)
            AppLogger.Info(Src, $"Signal filtered: recent candles are too small ({smallCount}/{RecentIndecisionLookback})");

        return indecisive;
    }

    private int CountExpressiveCandles()
    {
        int expressive = 0;
        int start = Math.Max(0, _tickCandles.Count - CandleFlowLookback);
        for (int i = start; i < _tickCandles.Count; i++)
        {
            var candle = _tickCandles[i];
            if (GetBodyRatio(candle) >= MinCandleBodyRatio)
                expressive++;
        }
        return expressive;
    }

    private static double GetBodyRatio(CandleData candle)
    {
        decimal range = candle.High - candle.Low;
        return range > 0 ? (double)(Math.Abs(candle.Close - candle.Open) / range) : 0;
    }

    private bool IsDirectionConfirmed(SignalDirection direction)
    {
        if (_ticks.Count < DirectionLookback + 1) return true;

        int ups = 0;
        int downs = 0;
        for (int i = _ticks.Count - DirectionLookback; i < _ticks.Count; i++)
        {
            if (_ticks[i] > _ticks[i - 1]) ups++;
            else if (_ticks[i] < _ticks[i - 1]) downs++;
        }

        return direction switch
        {
            SignalDirection.Call => downs < DirectionLookback,
            SignalDirection.Put => ups < DirectionLookback,
            _ => true
        };
    }

    private void EvaluateSignals()
    {
        if (_cooldownTicks > 0)
        {
            _cooldownTicks--;
            AppLogger.Info(Src, $"Candle died during cooldown (remaining: {_cooldownTicks})");
            return;
        }

        if (IsSignalContextBlocked()) return;

        if (_ticks.Count < _ticksPerCandle) return;

        var signals = new List<IndicatorSignal>();
        foreach (var indicator in _indicators)
        {
            var sig = indicator.Evaluate(_ticks);
            signals.Add(sig);
        }

        if (_tickCandles.Count >= 3)
        {
            var seqSignal = _candleSequenceIndicator.Evaluate(_tickCandles);
            if (seqSignal.Direction != SignalDirection.None)
                signals.Add(seqSignal);
        }

        double callScore = 0, putScore = 0;
        int callCount = 0, putCount = 0;
        var callReasons = new List<string>();
        var putReasons = new List<string>();

        foreach (var s in signals)
        {
            if (s.Direction == SignalDirection.Call && s.Strength > 0)
            {
                callScore += s.Strength;
                callCount++;
                callReasons.Add(s.Reason);
            }
            else if (s.Direction == SignalDirection.Put && s.Strength > 0)
            {
                putScore += s.Strength;
                putCount++;
                putReasons.Add(s.Reason);
            }
        }

        // Flat filter: if too many ticks are flat, skip
        if (_flatFilter)
        {
            int nonFlat = 0;
            int start = Math.Max(0, _ticks.Count - 20);
            for (int i = start; i < _ticks.Count; i++)
            {
                if (i > 0 && _ticks[i] != _ticks[i - 1])
                    nonFlat++;
            }
            double flatRatio = 1.0 - (double)nonFlat / Math.Min(20, _ticks.Count - start);
            if (flatRatio > 0.60)
            {
                AppLogger.Info(Src, $"Signal filtered: flat market ({flatRatio:P0} flat ticks)");
                return;
            }
        }

        SignalDirection direction;
        double score;
        int agreement;
        string reasons;

        if (callScore >= putScore && callCount >= _minAgreement)
        {
            direction = SignalDirection.Call;
            score = callCount > 0 ? callScore / callCount : 0;
            agreement = callCount;
            reasons = string.Join(" + ", callReasons);
        }
        else if (putScore > callScore && putCount >= _minAgreement)
        {
            direction = SignalDirection.Put;
            score = putCount > 0 ? putScore / putCount : 0;
            agreement = putCount;
            reasons = string.Join(" + ", putReasons);
        }
        else
        {
            AppLogger.Info(Src, $"Signal suppressed: call({callScore:F2}/{callCount}) put({putScore:F2}/{putCount}) need ≥{_minAgreement} agreement");
            return;
        }

        if (score < _threshold)
        {
            AppLogger.Info(Src, $"Signal suppressed: score {score:P0} below threshold {_threshold:P0}");
            return;
        }

        if (!IsDirectionConfirmed(direction))
        {
            AppLogger.Info(Src, $"Signal suppressed: recent ticks contradict {direction}");
            return;
        }

        var tradeSignal = new TradeSignal
        {
            Direction = direction,
            Confidence = score,
            Reason = reasons,
            Timestamp = DateTimeOffset.UtcNow,
            ContributingIndicators = signals
                .Where(s => s.Direction == direction && s.Strength > 0)
                .Select(s => s.Type)
                .ToList()
        };

        AppLogger.Info(Src, $"Signal: {direction} (score: {score:P0}, agree: {agreement}/{_minAgreement}+) — {tradeSignal.Reason}");
        _cooldownTicks = _cooldownSetting;
        SignalGenerated?.Invoke(this, tradeSignal);
    }
}
