using Excalibur5.Models.Strategy;

namespace Excalibur5.Services.Strategy;

public sealed class WeightedSignalAggregator
{
    private const int HistoryWindow = 20;

    private readonly Dictionary<IndicatorType, double> _baseWeights = new()
    {
        [IndicatorType.EmaCrossover] = 1.0,
        [IndicatorType.Rsi] = 1.0,
        [IndicatorType.SupportResistance] = 1.2,
        [IndicatorType.Macd] = 1.1,
        [IndicatorType.BollingerBands] = 1.0,
        [IndicatorType.Atr] = 0.5,
        [IndicatorType.CandlePattern] = 1.3,
        [IndicatorType.Momentum] = 0.9
    };

    private readonly Dictionary<IndicatorType, Queue<bool>> _history = new();

    public double CalculateScore(IReadOnlyList<IndicatorSignal> signals, SignalDirection direction)
    {
        double totalWeight = 0;
        double weightedSum = 0;

        foreach (var signal in signals)
        {
            if (signal.Direction != direction && signal.Direction != SignalDirection.None)
                continue;

            double baseWeight = _baseWeights.GetValueOrDefault(signal.Type, 1.0);
            double winRate = GetWinRate(signal.Type);
            double effectiveWeight = baseWeight * (0.5 + winRate);

            if (signal.Direction == direction)
            {
                weightedSum += effectiveWeight * signal.Strength;
                totalWeight += effectiveWeight;
            }
            else if (signal.Strength < 0)
            {
                // Negative strength (e.g., Doji) penalizes the score
                weightedSum += signal.Strength * 0.5;
            }
        }

        if (totalWeight == 0) return 0;
        return Math.Clamp(weightedSum / totalWeight, 0, 1.0);
    }

    public SignalDirection GetDominantDirection(IReadOnlyList<IndicatorSignal> signals)
    {
        double callScore = 0, putScore = 0;
        int callCount = 0, putCount = 0;

        foreach (var signal in signals)
        {
            if (signal.Direction == SignalDirection.Call)
            {
                callScore += signal.Strength;
                callCount++;
            }
            else if (signal.Direction == SignalDirection.Put)
            {
                putScore += signal.Strength;
                putCount++;
            }
        }

        if (callCount >= 2 && callScore > putScore) return SignalDirection.Call;
        if (putCount >= 2 && putScore > callScore) return SignalDirection.Put;
        return SignalDirection.None;
    }

    public void RecordResult(IReadOnlyList<IndicatorType> contributingIndicators, bool won)
    {
        foreach (var type in contributingIndicators)
        {
            if (!_history.TryGetValue(type, out var queue))
            {
                queue = new Queue<bool>();
                _history[type] = queue;
            }

            queue.Enqueue(won);
            if (queue.Count > HistoryWindow)
                queue.Dequeue();
        }
    }

    public double GetWinRate(IndicatorType type)
    {
        if (!_history.TryGetValue(type, out var queue) || queue.Count == 0)
            return 0.5;

        return (double)queue.Count(w => w) / queue.Count;
    }

    public void Reset() => _history.Clear();
}
