using Excalibur5.Models;
using Excalibur5.Models.Strategy;

namespace Excalibur5.Services.Strategy;

public sealed class StrategyEngine : IStrategyEngine
{
    private const string Src = "StrategyEngine";
    private const int MinCandlesRequired = 50;

    private readonly List<CandleData> _candles = new();
    private readonly List<IIndicator> _indicators = new();
    private readonly WeightedSignalAggregator _aggregator = new();
    private readonly SignalFilter _filter = new();
    private StrategyConfig _config = new();
    private long _lastSignalEpoch;

    public event EventHandler<TradeSignal>? SignalGenerated;
    public bool IsRunning { get; private set; }
    public WeightedSignalAggregator Aggregator => _aggregator;
    public SignalFilter Filter => _filter;

    public void Start(StrategyConfig config)
    {
        _config = config;
        _candles.Clear();
        _indicators.Clear();
        _lastSignalEpoch = 0;
        _filter.Reset();

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
        if (candle.Epoch <= _lastSignalEpoch) return;

        EvaluateSignals(candle.Epoch);
    }

    public void RecordTradeResult(IReadOnlyList<IndicatorType> contributors, bool won)
    {
        _aggregator.RecordResult(contributors, won);
        if (!won)
            _filter.OnLoss();
    }

    private void EvaluateSignals(long currentEpoch)
    {
        var signals = new List<IndicatorSignal>();
        foreach (var indicator in _indicators)
        {
            var signal = indicator.Evaluate(_candles);
            signals.Add(new IndicatorSignal
            {
                Direction = signal.Direction,
                Strength = signal.Strength,
                Reason = signal.Reason,
                Type = indicator.Type
            });
        }

        var direction = _aggregator.GetDominantDirection(signals);
        if (direction == SignalDirection.None) return;
        if (!IsDirectionAllowed(direction)) return;

        double score = _aggregator.CalculateScore(signals, direction);
        if (score < _config.ConfidenceThreshold) return;

        if (_filter.ShouldFilter(_candles, direction, score, _config))
            return;

        var reasons = string.Join(" + ", signals
            .Where(s => s.Direction == direction && s.Strength > 0)
            .Select(s => s.Reason));

        var contributingTypes = signals
            .Where(s => s.Direction == direction && s.Strength > 0)
            .Select(s => s.Type)
            .ToList();

        EmitSignal(direction, score, reasons, currentEpoch, contributingTypes);
    }

    private bool IsDirectionAllowed(SignalDirection direction)
    {
        if (_config.AllowedDirection == SignalDirection.None) return true;
        return _config.AllowedDirection == direction;
    }

    private void EmitSignal(SignalDirection direction, double confidence, string reason, long epoch, List<IndicatorType> contributors)
    {
        _lastSignalEpoch = epoch;
        var signal = new TradeSignal
        {
            Direction = direction,
            Confidence = confidence,
            Reason = reason,
            Timestamp = DateTimeOffset.UtcNow,
            ContributingIndicators = contributors
        };

        AppLogger.Info(Src, $"Signal: {direction} (score: {confidence:P0}) — {reason}");
        SignalGenerated?.Invoke(this, signal);
    }

    private static IIndicator? CreateIndicator(IndicatorType type) => type switch
    {
        IndicatorType.EmaCrossover => new EmaIndicator(),
        IndicatorType.Rsi => new RsiIndicator(),
        IndicatorType.SupportResistance => new SupportResistanceIndicator(),
        IndicatorType.Macd => new MacdIndicator(),
        IndicatorType.BollingerBands => new BollingerIndicator(),
        IndicatorType.Atr => new AtrIndicator(),
        IndicatorType.CandlePattern => new CandlePatternIndicator(),
        IndicatorType.Momentum => new MomentumIndicator(),
        _ => null
    };
}
