using Excalibur5.Models;
using Excalibur5.Models.Strategy;

namespace Excalibur5.Services.Strategy;

public sealed class CandleDynamicsEngine
{
    private const string Src = "CandleDynamics";
    private const int TicksPerCandle = 10;
    private const int MaxTicks = 200;
    private const int MinCandlesRequired = 5;

    private readonly CandleDynamicsAnalyzer _analyzer = new();
    private readonly List<decimal> _ticks = new(MaxTicks);
    private readonly List<decimal> _currentCandleTicks = new(TicksPerCandle);
    private readonly List<CandleData> _tickCandles = new();
    private int _cooldownTicks;
    private int _cooldownSetting = 10;
    private double _threshold = 0.55;
    private int _minStreak = 3;
    private int _minSignals = 2;
    private bool _isRunning;
    private int _consecutiveLosses;
    private bool _firstHalfEmitted;

    public event EventHandler<TradeSignal>? SignalGenerated;
    public bool IsRunning => _isRunning;

    public void Start(int cooldown, double threshold, int minStreak)
    {
        _cooldownSetting = cooldown;
        _threshold = threshold;
        _minStreak = minStreak;
        _cooldownTicks = 0;
        _consecutiveLosses = 0;
        _ticks.Clear();
        _currentCandleTicks.Clear();
        _tickCandles.Clear();
        _analyzer.Reset();
        _firstHalfEmitted = false;
        _isRunning = true;
        AppLogger.Info(Src, $"Started — cooldown={cooldown}, threshold={threshold:P0}, minStreak={minStreak}");
    }

    public void Stop()
    {
        _isRunning = false;
        _ticks.Clear();
        _currentCandleTicks.Clear();
        _tickCandles.Clear();
        _analyzer.Reset();
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

        _currentCandleTicks.Add(price);

        if (_cooldownTicks > 0)
        {
            _cooldownTicks--;
            if (_currentCandleTicks.Count >= TicksPerCandle)
            {
                AppLogger.Info(Src, $"Candle formed during cooldown (remaining: {_cooldownTicks})");
                FormCandle();
            }
            return;
        }

        if (_currentCandleTicks.Count == 5 && !_firstHalfEmitted)
        {
            EvaluateFirstHalf();
        }

        if (_currentCandleTicks.Count >= TicksPerCandle)
        {
            AppLogger.Info(Src, $"Candle formed — evaluating (total candles: {_analyzer.CandleCount + 1})");
            FormCandle();
            EvaluateSignals();
        }
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

        // Build candles from history
        for (int i = 0; i <= _ticks.Count - TicksPerCandle; i += TicksPerCandle)
        {
            var chunk = _ticks.GetRange(i, TicksPerCandle);
            var candle = BuildCandleFromTicks(chunk);
            _analyzer.UpdateWithCandle(candle, chunk);
        }

        AppLogger.Info(Src, $"History fed: {_ticks.Count} ticks, {_analyzer.CandleCount} candles analyzed");
    }

    private void FormCandle()
    {
        var candle = BuildCandleFromTicks(_currentCandleTicks);
        _analyzer.UpdateWithCandle(candle, _currentCandleTicks);
        _currentCandleTicks.Clear();
        _firstHalfEmitted = false;
    }

    private static CandleData BuildCandleFromTicks(IReadOnlyList<decimal> ticks)
    {
        decimal o = ticks[0];
        decimal c = ticks[^1];
        decimal h = ticks[0], l = ticks[0];
        for (int i = 1; i < ticks.Count; i++)
        {
            if (ticks[i] > h) h = ticks[i];
            if (ticks[i] < l) l = ticks[i];
        }
        return new CandleData { Open = o, High = h, Low = l, Close = c, Epoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
    }

    private void EvaluateFirstHalf()
    {
        var signal = _analyzer.GetFirstHalfSignal(_currentCandleTicks);
        if (signal.Direction == SignalDirection.None || signal.Strength < _threshold)
            return;

        _firstHalfEmitted = true;
        var tradeSignal = new TradeSignal
        {
            Direction = signal.Direction,
            Confidence = signal.Strength,
            Reason = signal.Reason,
            Timestamp = DateTimeOffset.UtcNow,
            ContributingIndicators = new List<IndicatorType> { IndicatorType.CandleDynamics }
        };

        AppLogger.Info(Src, $"First-half signal: {signal.Direction} (str: {signal.Strength:P0}) -- {signal.Reason}");
        _cooldownTicks = _cooldownSetting;
        SignalGenerated?.Invoke(this, tradeSignal);
    }

    private void EvaluateSignals()
    {
        if (_analyzer.CandleCount < MinCandlesRequired) return;

        var streakSignal = _analyzer.GetStreakSignal(_minStreak);
        var transitionSignal = _analyzer.GetTransitionSignal();
        var velocitySignal = _analyzer.GetVelocitySignal();
        var internalTickSignal = _analyzer.GetInternalTickSignal();

        var signals = new[] { streakSignal, transitionSignal, velocitySignal, internalTickSignal };

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

        SignalDirection direction;
        double score;
        int count;
        List<string> reasons;

        // Strong streak (strength >= 0.55, i.e. 4+ candles) can trigger alone
        bool hasStrongStreak = streakSignal.Strength >= 0.55;
        int requiredSignals = hasStrongStreak ? 1 : _minSignals;

        if (callScore >= putScore && callCount >= requiredSignals)
        {
            direction = SignalDirection.Call;
            score = callScore / callCount;
            count = callCount;
            reasons = callReasons;
        }
        else if (putScore > callScore && putCount >= requiredSignals)
        {
            direction = SignalDirection.Put;
            score = putScore / putCount;
            count = putCount;
            reasons = putReasons;
        }
        else
        {
            return;
        }

        if (score < _threshold) return;

        var tradeSignal = new TradeSignal
        {
            Direction = direction,
            Confidence = score,
            Reason = string.Join(" + ", reasons),
            Timestamp = DateTimeOffset.UtcNow,
            ContributingIndicators = new List<IndicatorType> { IndicatorType.CandleDynamics }
        };

        AppLogger.Info(Src, $"Signal: {direction} (score: {score:P0}, agree: {count}/4) -- {tradeSignal.Reason}");
        _cooldownTicks = _cooldownSetting;
        SignalGenerated?.Invoke(this, tradeSignal);
    }
}
