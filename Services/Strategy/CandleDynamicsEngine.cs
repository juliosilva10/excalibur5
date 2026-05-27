using Excalibur5.Models;
using Excalibur5.Models.Strategy;

namespace Excalibur5.Services.Strategy;

public sealed class CandleDynamicsEngine
{
    private const string Src = "CandleDynamics";
    private const int MaxTicks = 200;
    private const int MinCandlesRequired = 5;

    private readonly CandleDynamicsAnalyzer _analyzer = new();
    private readonly List<decimal> _ticks = new(MaxTicks);
    private readonly List<decimal> _currentCandleTicks = new();
    private TickCandleManager _candleManager = null!;
    private int _cooldownTicks;
    private int _cooldownSetting = 10;
    private double _threshold = 0.55;
    private int _minStreak = 3;
    private int _minSignals = 2;
    private bool _isRunning;
    private int _consecutiveLosses;
    private int _ticksPerCandle = 10;

    public event EventHandler<TradeSignal>? SignalGenerated;
    public bool IsRunning => _isRunning;

    public void Start(int ticksPerCandle, int cooldown, double threshold, int minStreak)
    {
        _ticksPerCandle = ticksPerCandle;
        _cooldownSetting = cooldown;
        _threshold = threshold;
        _minStreak = minStreak;
        _cooldownTicks = 0;
        _consecutiveLosses = 0;
        _ticks.Clear();
        _currentCandleTicks.Clear();
        _analyzer.Reset();
        _isRunning = true;

        if (_candleManager != null)
            _candleManager.CandleDied -= OnCandleDied;

        _candleManager = new TickCandleManager(ticksPerCandle);
        _candleManager.CandleDied += OnCandleDied;

        AppLogger.Info(Src, $"Started — ticksPerCandle={ticksPerCandle}, cooldown={cooldown}, threshold={threshold:P0}, minStreak={minStreak}");
    }

    public void Stop()
    {
        _isRunning = false;
        _ticks.Clear();
        _currentCandleTicks.Clear();
        _analyzer.Reset();
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

        if (candleDied && _candleManager.TotalCandlesFormed > 0)
        {
            if (_cooldownTicks > 0)
            {
                _cooldownTicks--;
                AppLogger.Info(Src, $"Candle died during cooldown (remaining: {_cooldownTicks})");
                return;
            }

            AppLogger.Info(Src, $"Candle died — evaluating (total candles: {_candleManager.TotalCandlesFormed})");
            EvaluateSignals();
        }
    }

    private void OnCandleDied(object? sender, CandleDiedEventArgs e)
    {
        var candle = new CandleData
        {
            Epoch = e.BirthEpoch,
            Open = e.Open,
            High = e.High,
            Low = e.Low,
            Close = e.Close
        };

        _analyzer.UpdateWithCandle(candle, _currentCandleTicks);
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
            int progressiveCooldown = Math.Min(_cooldownSetting * (1 << Math.Min(_consecutiveLosses, 2)), _cooldownSetting * 4);
            _cooldownTicks = progressiveCooldown;
            AppLogger.Info(Src, $"Progressive cooldown: {progressiveCooldown} candles (losses: {_consecutiveLosses})");
        }
    }

    public void FeedHistory(IReadOnlyList<decimal> history)
    {
        _ticks.Clear();
        int start = Math.Max(0, history.Count - MaxTicks);
        for (int i = start; i < history.Count; i++)
            _ticks.Add(history[i]);

        var candles = _candleManager.FeedHistory(history, null);
        _analyzer.Reset();

        for (int i = 0; i < candles.Count; i++)
        {
            var chunk = history
                .Skip(i * _ticksPerCandle)
                .Take(_ticksPerCandle)
                .ToList();
            _analyzer.UpdateWithCandle(candles[i], chunk);
        }

        RestoreCurrentCandleTicks(history, candles.Count);
        AppLogger.Info(Src, $"History fed: {_ticks.Count} ticks, {candles.Count} candles analyzed");
    }

    private void RestoreCurrentCandleTicks(IReadOnlyList<decimal> history, int completedCandles)
    {
        _currentCandleTicks.Clear();
        int firstOpenTick = completedCandles * _ticksPerCandle;
        for (int i = firstOpenTick; i < history.Count; i++)
            _currentCandleTicks.Add(history[i]);
    }

    private void EvaluateSignals()
    {
        if (_analyzer.CandleCount < MinCandlesRequired)
        {
            AppLogger.Info(Src, $"Signal suppressed: need {MinCandlesRequired} candles, have {_analyzer.CandleCount}");
            return;
        }

        if (_analyzer.IsRecentMarketIndecisive())
        {
            AppLogger.Info(Src, "Signal suppressed: recent candles are too small / indecisive");
            return;
        }

        if (!_analyzer.AreRecentCandlesStrong())
        {
            AppLogger.Info(Src, "Signal suppressed: previous candles lack strength");
            return;
        }

        var streakSignal = _analyzer.GetStreakSignal(_minStreak);
        var transitionSignal = _analyzer.GetTransitionSignal();
        var velocitySignal = _analyzer.GetVelocitySignal();
        var internalTickSignal = _analyzer.GetInternalTickSignal();
        var patternForecastSignal = _analyzer.GetPatternForecastSignal();
        var candleForceSignal = _analyzer.GetCandleForceSignal();

        var signals = new[] { streakSignal, transitionSignal, velocitySignal, internalTickSignal, patternForecastSignal, candleForceSignal };

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

        int callRequiredSignals = IsDecisiveForce(candleForceSignal, SignalDirection.Call) ? 1 : _minSignals;
        int putRequiredSignals = IsDecisiveForce(candleForceSignal, SignalDirection.Put) ? 1 : _minSignals;

        if (callScore >= putScore && callCount >= callRequiredSignals)
        {
            direction = SignalDirection.Call;
            score = callScore / callCount;
            count = callCount;
            reasons = callReasons;
        }
        else if (putScore > callScore && putCount >= putRequiredSignals)
        {
            direction = SignalDirection.Put;
            score = putScore / putCount;
            count = putCount;
            reasons = putReasons;
        }
        else
        {
            AppLogger.Info(Src, $"Signal suppressed: call({callScore:F2}/{callCount}) put({putScore:F2}/{putCount}) need {Math.Min(callRequiredSignals, putRequiredSignals)}");
            return;
        }

        if (score < _threshold)
        {
            AppLogger.Info(Src, $"Signal suppressed: score {score:P0} below threshold {_threshold:P0}");
            return;
        }

        if (!_analyzer.IsDirectionSupportedByCandleForce(direction))
        {
            AppLogger.Info(Src, $"Signal suppressed: candle force does not support {direction}");
            return;
        }

        var tradeSignal = new TradeSignal
        {
            Direction = direction,
            Confidence = score,
            Reason = string.Join(" + ", reasons),
            Timestamp = DateTimeOffset.UtcNow,
            ContributingIndicators = new List<IndicatorType> { IndicatorType.CandleDynamics }
        };

        AppLogger.Info(Src, $"Signal: {direction} (score: {score:P0}, agree: {count}/6) -- {tradeSignal.Reason}");
        _cooldownTicks = _cooldownSetting;
        SignalGenerated?.Invoke(this, tradeSignal);
    }

    private static bool IsDecisiveForce(IndicatorSignal signal, SignalDirection direction)
    {
        return signal.Direction == direction && signal.Strength >= 0.72;
    }
}
