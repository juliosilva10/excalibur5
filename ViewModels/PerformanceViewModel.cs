using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Excalibur5.Models;

namespace Excalibur5.ViewModels;

public partial class PerformanceViewModel : ObservableObject
{
    [ObservableProperty] private bool _isPerformanceVisible;
    [ObservableProperty] private int _totalOperations;
    [ObservableProperty] private LargestStakeInfo? _largestStake;
    [ObservableProperty] private DrawdownInfo? _maxDrawdown;
    [ObservableProperty] private LossStreakInfo? _longestLossStreak;

    public ObservableCollection<StrategyPerformance> StrategyStats { get; } = new();

    private readonly List<BalancePoint> _balanceHistory = new();
    private readonly Dictionary<long, TickSnapshot> _tickSnapshots = new();
    private readonly Dictionary<long, CandleSnapshot> _candleSnapshots = new();
    private readonly List<decimal> _currentLossStreak = new();
    private decimal _peakBalance;
    private decimal _maxDrawdownValue;
    private TickSnapshot? _lastTickSnapshot;
    private CandleSnapshot? _lastCandleSnapshot;

    [RelayCommand]
    private void TogglePerformance()
    {
        IsPerformanceVisible = !IsPerformanceVisible;
    }

    public void OnTradeOpened(long contractId, string market, IList<decimal> chartValues,
                              IList<long> chartEpochs, IList<TickDirection> chartDirections,
                              ChartType chartType, IList<CandleData> tickCandles, IList<CandleData> candles)
    {
        int count = Math.Min(30, chartValues.Count);
        int start = chartValues.Count - count;

        var snapshot = new TickSnapshot
        {
            Values = chartValues.Skip(start).Take(count).ToList(),
            Epochs = chartEpochs.Skip(start).Take(count).ToList(),
            Directions = chartDirections.Skip(start).Take(count).ToList()
        };

        _tickSnapshots[contractId] = snapshot;
        _lastTickSnapshot = snapshot;

        var candleSnap = CaptureCandleSnapshot(chartType, tickCandles, candles);
        if (candleSnap != null)
        {
            _candleSnapshots[contractId] = candleSnap;
            _lastCandleSnapshot = candleSnap;
        }
    }

    public void OnBalanceUpdated(decimal balance)
    {
        _balanceHistory.Add(new BalancePoint { Time = DateTime.Now, Balance = balance });

        if (_balanceHistory.Count == 1 || balance > _peakBalance)
            _peakBalance = balance;

        var currentDrawdown = _peakBalance - balance;
        if (currentDrawdown > _maxDrawdownValue && currentDrawdown > 0)
        {
            _maxDrawdownValue = currentDrawdown;
            MaxDrawdown = new DrawdownInfo
            {
                DrawdownValue = currentDrawdown,
                OccurredAt = DateTime.Now,
                PeakBalance = _peakBalance,
                TroughBalance = balance,
                TickSnapshot = _lastTickSnapshot,
                CandleSnapshot = _lastCandleSnapshot
            };
        }
    }

    public void CaptureTickSnapshot(IList<decimal> chartValues, IList<long> chartEpochs,
                                     IList<TickDirection> chartDirections,
                                     ChartType chartType, IList<CandleData> tickCandles, IList<CandleData> candles)
    {
        int count = Math.Min(30, chartValues.Count);
        int start = chartValues.Count - count;

        _lastTickSnapshot = new TickSnapshot
        {
            Values = chartValues.Skip(start).Take(count).ToList(),
            Epochs = chartEpochs.Skip(start).Take(count).ToList(),
            Directions = chartDirections.Skip(start).Take(count).ToList()
        };

        var candleSnap = CaptureCandleSnapshot(chartType, tickCandles, candles);
        if (candleSnap != null)
            _lastCandleSnapshot = candleSnap;
    }

    public void OnTradeCompleted(TradeHistoryItem trade)
    {
        TotalOperations++;
        UpdateLargestStake(trade);
        UpdateStrategyStats(trade);
        UpdateLossStreak(trade);
    }

    private static CandleSnapshot? CaptureCandleSnapshot(ChartType chartType, IList<CandleData> tickCandles, IList<CandleData> candles)
    {
        IList<CandleData> source;
        ChartSnapshotType snapshotType;

        if (chartType == ChartType.TickCandles && tickCandles.Count >= 2)
        {
            source = tickCandles;
            snapshotType = ChartSnapshotType.TickCandles;
        }
        else if (chartType == ChartType.Candles && candles.Count >= 2)
        {
            source = candles;
            snapshotType = ChartSnapshotType.Candles;
        }
        else if (tickCandles.Count >= 2)
        {
            source = tickCandles;
            snapshotType = ChartSnapshotType.TickCandles;
        }
        else if (candles.Count >= 2)
        {
            source = candles;
            snapshotType = ChartSnapshotType.Candles;
        }
        else
        {
            return null;
        }

        int count = Math.Min(20, source.Count);
        int start = source.Count - count;
        var captured = source.Skip(start).Take(count).Select(c => new CandleData
        {
            Epoch = c.Epoch, Open = c.Open, High = c.High, Low = c.Low, Close = c.Close
        }).ToList();

        return new CandleSnapshot
        {
            Candles = captured,
            Type = snapshotType,
            HighlightIndex = captured.Count - 1
        };
    }

    private void UpdateLargestStake(TradeHistoryItem trade)
    {
        if (LargestStake == null || trade.Stake > LargestStake.Trade.Stake)
        {
            _tickSnapshots.TryGetValue(trade.ContractId, out var snapshot);
            _candleSnapshots.TryGetValue(trade.ContractId, out var candleSnap);
            LargestStake = new LargestStakeInfo
            {
                Trade = trade,
                Market = trade.Market,
                TickSnapshot = snapshot,
                CandleSnapshot = candleSnap
            };
        }
    }

    private void UpdateLossStreak(TradeHistoryItem trade)
    {
        bool lost = !trade.ProfitLoss.HasValue || trade.ProfitLoss.Value <= 0;

        if (lost)
        {
            _currentLossStreak.Add(trade.Stake);
        }

        if (!lost || _currentLossStreak.Count > (LongestLossStreak?.Length ?? 0))
        {
            if (_currentLossStreak.Count > (LongestLossStreak?.Length ?? 0))
            {
                _tickSnapshots.TryGetValue(trade.ContractId, out var tickSnap);
                _candleSnapshots.TryGetValue(trade.ContractId, out var candleSnap);
                LongestLossStreak = new LossStreakInfo
                {
                    Length = _currentLossStreak.Count,
                    Stakes = new List<decimal>(_currentLossStreak),
                    TotalLost = _currentLossStreak.Sum(),
                    TickSnapshot = tickSnap,
                    CandleSnapshot = candleSnap
                };
            }
        }

        if (!lost)
            _currentLossStreak.Clear();
    }

    private void UpdateStrategyStats(TradeHistoryItem trade)
    {
        var strategyName = string.IsNullOrEmpty(trade.Estrategia) ? "Manual" : trade.Estrategia;
        var existing = StrategyStats.FirstOrDefault(s => s.StrategyName == strategyName);
        bool won = trade.ProfitLoss.HasValue && trade.ProfitLoss.Value > 0;
        decimal profit = trade.ProfitLoss ?? 0;

        if (existing != null)
        {
            var idx = StrategyStats.IndexOf(existing);
            StrategyStats[idx] = new StrategyPerformance
            {
                StrategyName = strategyName,
                TotalTrades = existing.TotalTrades + 1,
                Wins = existing.Wins + (won ? 1 : 0),
                Losses = existing.Losses + (won ? 0 : 1),
                TotalProfit = existing.TotalProfit + profit
            };
        }
        else
        {
            StrategyStats.Add(new StrategyPerformance
            {
                StrategyName = strategyName,
                TotalTrades = 1,
                Wins = won ? 1 : 0,
                Losses = won ? 0 : 1,
                TotalProfit = profit
            });
        }
    }
}
