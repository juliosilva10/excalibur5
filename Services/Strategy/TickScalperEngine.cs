using Excalibur5.Models.Strategy;
using Excalibur5.Services.Strategy.TickIndicators;

namespace Excalibur5.Services.Strategy;

public sealed class TickScalperEngine
{
    private const string Src = "TickScalper";
    private const int MinTicksRequired = 30;
    private const int MaxTicks = 200;

    private readonly List<decimal> _ticks = new(MaxTicks);
    private readonly List<ITickIndicator> _indicators = new();
    private int _cooldownTicks;
    private int _cooldownSetting = 5;
    private double _threshold = 0.70;
    private int _minAgreement = 2;
    private bool _flatFilter = true;
    private bool _isRunning;

    public event EventHandler<TradeSignal>? SignalGenerated;
    public bool IsRunning => _isRunning;

    public void Start(int cooldown, double threshold, int minAgreement, bool flatFilter)
    {
        _cooldownSetting = cooldown;
        _threshold = threshold;
        _minAgreement = minAgreement;
        _flatFilter = flatFilter;
        _cooldownTicks = 0;
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
        foreach (var ind in _indicators)
            ind.Reset();
        AppLogger.Info(Src, "Stopped");
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

        EvaluateSignals();
    }

    public void SetCooldown()
    {
        _cooldownTicks = _cooldownSetting;
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

    private void EvaluateSignals()
    {
        var signals = new List<IndicatorSignal>();
        foreach (var indicator in _indicators)
        {
            var signal = indicator.Evaluate(_ticks);
            signals.Add(signal);
        }

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

        var reasons = string.Join(" + ", signals
            .Where(s => s.Direction == direction && s.Strength > 0)
            .Select(s => s.Reason));

        var contributors = signals
            .Where(s => s.Direction == direction && s.Strength > 0)
            .Select(s => s.Type)
            .ToList();

        var tradeSignal = new TradeSignal
        {
            Direction = direction,
            Confidence = score,
            Reason = reasons,
            Timestamp = DateTimeOffset.UtcNow,
            ContributingIndicators = contributors
        };

        AppLogger.Info(Src, $"Signal: {direction} (score: {score:P0}, agree: {count}/5) — {reasons}");
        SignalGenerated?.Invoke(this, tradeSignal);
    }
}
