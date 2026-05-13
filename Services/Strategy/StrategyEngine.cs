using Excalibur5.Models;
using Excalibur5.Models.Strategy;

namespace Excalibur5.Services.Strategy;

/// <summary>
/// Evaluates candles against enabled indicators and emits trade signals
/// when confluence meets the confidence threshold.
/// </summary>
public sealed class StrategyEngine : IStrategyEngine
{
    private const string Src = "StrategyEngine";
    private const int MinCandlesRequired = 25;

    private readonly List<CandleData> _candles = new();
    private readonly List<IIndicator> _indicators = new();
    private StrategyConfig _config = new();
    private long _lastSignalEpoch;

    public event EventHandler<TradeSignal>? SignalGenerated;
    public bool IsRunning { get; private set; }

    public void Start(StrategyConfig config)
    {
        _config = config;
        _candles.Clear();
        _indicators.Clear();
        _lastSignalEpoch = 0;

        foreach (var type in config.EnabledIndicators)
        {
            var indicator = CreateIndicator(type);
            if (indicator != null)
                _indicators.Add(indicator);
        }

        IsRunning = true;
        AppLogger.Info(Src, $"Started with {_indicators.Count} indicators, threshold={config.ConfidenceThreshold:P0}");
    }

    public void Stop()
    {
        IsRunning = false;
        _candles.Clear();
        foreach (var ind in _indicators)
            ind.Reset();
        AppLogger.Info(Src, "Stopped");
    }

    public void FeedCandle(CandleData candle)
    {
        if (!IsRunning) return;

        // Update or add candle
        if (_candles.Count > 0 && _candles[^1].Epoch == candle.Epoch)
        {
            _candles[^1] = candle;
        }
        else
        {
            _candles.Add(candle);
            if (_candles.Count > 500)
                _candles.RemoveAt(0);
        }

        if (_candles.Count < MinCandlesRequired) return;

        // Cooldown: don't signal on the same candle
        if (candle.Epoch <= _lastSignalEpoch) return;

        EvaluateSignals(candle.Epoch);
    }

    private void EvaluateSignals(long currentEpoch)
    {
        var callSignals = new List<IndicatorSignal>();
        var putSignals = new List<IndicatorSignal>();

        foreach (var indicator in _indicators)
        {
            var signal = indicator.Evaluate(_candles);
            if (signal.Direction == SignalDirection.Call)
                callSignals.Add(signal);
            else if (signal.Direction == SignalDirection.Put)
                putSignals.Add(signal);
        }

        // Check confluence: need 2+ indicators agreeing
        if (callSignals.Count >= 2 && IsDirectionAllowed(SignalDirection.Call))
        {
            double confidence = callSignals.Average(s => s.Strength);
            if (confidence >= _config.ConfidenceThreshold)
            {
                var reasons = string.Join(" + ", callSignals.Select(s => s.Reason));
                EmitSignal(SignalDirection.Call, confidence, reasons, currentEpoch);
                return;
            }
        }

        if (putSignals.Count >= 2 && IsDirectionAllowed(SignalDirection.Put))
        {
            double confidence = putSignals.Average(s => s.Strength);
            if (confidence >= _config.ConfidenceThreshold)
            {
                var reasons = string.Join(" + ", putSignals.Select(s => s.Reason));
                EmitSignal(SignalDirection.Put, confidence, reasons, currentEpoch);
            }
        }
    }

    private bool IsDirectionAllowed(SignalDirection direction)
    {
        if (_config.AllowedDirection == SignalDirection.None) return true; // Both allowed
        return _config.AllowedDirection == direction;
    }

    private void EmitSignal(SignalDirection direction, double confidence, string reason, long epoch)
    {
        _lastSignalEpoch = epoch;
        var signal = new TradeSignal
        {
            Direction = direction,
            Confidence = confidence,
            Reason = reason,
            Timestamp = DateTimeOffset.UtcNow
        };

        AppLogger.Info(Src, $"Signal: {direction} (conf: {confidence:P0}) — {reason}");
        SignalGenerated?.Invoke(this, signal);
    }

    private static IIndicator? CreateIndicator(IndicatorType type) => type switch
    {
        IndicatorType.EmaCrossover => new EmaIndicator(),
        IndicatorType.Rsi => new RsiIndicator(),
        IndicatorType.SupportResistance => new SupportResistanceIndicator(),
        _ => null
    };
}
