using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Excalibur5.Models;

namespace Excalibur5.ViewModels;

public partial class PerformanceViewModel : ObservableObject
{
    private const double DrawdownTradeMatchSeconds = 90;

    [ObservableProperty] private bool _isPerformanceVisible;
    [ObservableProperty] private int _totalOperations;
    [ObservableProperty] private LargestStakeInfo? _largestStake;
    [ObservableProperty] private DrawdownInfo? _maxDrawdown;
    [ObservableProperty] private LossStreakInfo? _longestLossStreak;

    public ObservableCollection<StrategyPerformance> StrategyStats { get; } = new();

    private readonly List<BalancePoint> _balanceHistory = new();
    private readonly Dictionary<long, TickSnapshot> _tickSnapshots = new();
    private readonly Dictionary<long, CandleSnapshot> _candleSnapshots = new();
    private readonly Dictionary<long, CandleEntryAnchor> _entryCandleAnchors = new();
    private readonly HashSet<long> _settledTrades = new();
    private readonly List<decimal> _currentLossStreak = new();
    private decimal _peakBalance;
    private decimal _maxDrawdownValue;
    private TickSnapshot? _lastTickSnapshot;
    private CandleSnapshot? _lastCandleSnapshot;
    private TradeHistoryItem? _lastCompletedTrade;
    private long _longestLossStreakContractId;

    [RelayCommand]
    private void TogglePerformance()
    {
        IsPerformanceVisible = true;
    }

    private sealed record CandleEntryAnchor(int Index, ChartSnapshotType Type);

    public void ResetSession(decimal startingBalance)
    {
        TotalOperations = 0;
        LargestStake = null;
        MaxDrawdown = null;
        LongestLossStreak = null;
        StrategyStats.Clear();
        ClearSessionState(startingBalance);
    }

    public void OnTradeOpened(long contractId, string market, long entryEpoch,
                              int? entryCandleIndex, ChartSnapshotType? entryCandleType, IList<decimal> chartValues,
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

        var entryAnchor = CreateEntryAnchor(entryCandleIndex, entryCandleType);
        if (entryAnchor != null)
            _entryCandleAnchors[contractId] = entryAnchor;

        var candleSnap = CaptureCandleSnapshot(chartType, tickCandles, candles, entryAnchor);
        if (candleSnap != null)
        {
            var entryCandleSnap = AddEntryMarkerIndex(candleSnap, entryEpoch);
            _candleSnapshots[contractId] = entryCandleSnap;
            _lastCandleSnapshot = entryCandleSnap;
        }
    }

    public void OnTradeClosed(long contractId, ChartType chartType, IList<CandleData> tickCandles, IList<CandleData> candles)
    {
        if (!_entryCandleAnchors.TryGetValue(contractId, out var entryAnchor) && _candleSnapshots.ContainsKey(contractId))
            return;

        var candleSnap = CaptureCandleSnapshot(chartType, tickCandles, candles, entryAnchor);
        if (candleSnap == null) return;

        _candleSnapshots[contractId] = candleSnap;
        _lastCandleSnapshot = candleSnap;
    }

    public void OnBalanceUpdated(decimal balance)
    {
        _balanceHistory.Add(new BalancePoint { Time = DateTime.Now, Balance = balance });

        if (_balanceHistory.Count == 1 || balance > _peakBalance)
            _peakBalance = balance;

        var currentDrawdown = _peakBalance - balance;
        if (currentDrawdown > _maxDrawdownValue && currentDrawdown > 0)
        {
            var occurredAt = DateTime.Now;
            var snapshot = GetDrawdownSnapshot(occurredAt);
            _maxDrawdownValue = currentDrawdown;
            MaxDrawdown = new DrawdownInfo
            {
                DrawdownValue = currentDrawdown,
                OccurredAt = occurredAt,
                PeakBalance = _peakBalance,
                TroughBalance = balance,
                TickSnapshot = snapshot.TickSnapshot,
                CandleSnapshot = snapshot.CandleSnapshot
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
        bool firstSettlement = trade.ContractId <= 0 || _settledTrades.Add(trade.ContractId);
        if (firstSettlement)
            TotalOperations++;

        _lastCompletedTrade = trade;
        UpdateLargestStake(trade);
        RefreshMaxDrawdownSnapshot(trade);

        if (firstSettlement)
        {
            UpdateStrategyStats(trade);
            UpdateLossStreak(trade);
        }
        else
        {
            RefreshLossStreakSnapshot(trade);
        }

        _entryCandleAnchors.Remove(trade.ContractId);
    }

    private static CandleEntryAnchor? CreateEntryAnchor(int? index, ChartSnapshotType? type)
    {
        return index.HasValue && type.HasValue
            ? new CandleEntryAnchor(index.Value, type.Value)
            : null;
    }

    private void ClearSessionState(decimal startingBalance)
    {
        _balanceHistory.Clear();
        _tickSnapshots.Clear();
        _candleSnapshots.Clear();
        _entryCandleAnchors.Clear();
        _settledTrades.Clear();
        _currentLossStreak.Clear();
        _peakBalance = startingBalance;
        _maxDrawdownValue = 0;
        _lastTickSnapshot = null;
        _lastCandleSnapshot = null;
        _lastCompletedTrade = null;
        _longestLossStreakContractId = 0;
        _balanceHistory.Add(new BalancePoint { Time = DateTime.Now, Balance = startingBalance });
    }

    private static CandleSnapshot? CaptureCandleSnapshot(
        ChartType chartType, IList<CandleData> tickCandles, IList<CandleData> candles, CandleEntryAnchor? anchor = null)
    {
        IList<CandleData> source;
        ChartSnapshotType snapshotType;

        if (anchor != null && TryGetSnapshotSource(anchor.Type, tickCandles, candles, out source))
        {
            snapshotType = anchor.Type;
        }
        else if (chartType == ChartType.TickCandles && tickCandles.Count >= 2)
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

        int count = Math.Min(30, source.Count);
        int? anchorIndex = anchor?.Index;
        int start = GetSnapshotStart(source.Count, count, anchorIndex);
        var captured = source.Skip(start).Take(count).Select(c => new CandleData
        {
            Epoch = c.Epoch, Open = c.Open, High = c.High, Low = c.Low, Close = c.Close
        }).ToList();
        int? entryIndex = GetLocalAnchorIndex(anchorIndex, start, captured.Count);

        return new CandleSnapshot
        {
            Candles = captured,
            Type = snapshotType,
            HighlightIndex = entryIndex ?? captured.Count - 1,
            EntryPrice = null,
            ExitPrice = null,
            EntryIndex = entryIndex,
            ExitIndex = null,
            IsEntryAnchored = entryIndex.HasValue
        };
    }

    private static bool TryGetSnapshotSource(
        ChartSnapshotType type, IList<CandleData> tickCandles, IList<CandleData> candles, out IList<CandleData> source)
    {
        source = type == ChartSnapshotType.TickCandles ? tickCandles : candles;
        return source.Count >= 2;
    }

    private static int GetSnapshotStart(int totalCount, int snapshotCount, int? anchorIndex)
    {
        if (!anchorIndex.HasValue)
            return totalCount - snapshotCount;

        int centeredStart = anchorIndex.Value - snapshotCount / 2;
        return Math.Clamp(centeredStart, 0, Math.Max(0, totalCount - snapshotCount));
    }

    private static int? GetLocalAnchorIndex(int? anchorIndex, int start, int count)
    {
        if (!anchorIndex.HasValue) return null;

        int localIndex = anchorIndex.Value - start;
        return localIndex >= 0 && localIndex < count ? localIndex : null;
    }

    private void UpdateLargestStake(TradeHistoryItem trade)
    {
        if (LargestStake == null || trade.Stake > LargestStake.Trade.Stake || IsLargestStakeTrade(trade))
        {
            _tickSnapshots.TryGetValue(trade.ContractId, out var snapshot);
            _candleSnapshots.TryGetValue(trade.ContractId, out var candleSnap);
            var candleWithPrices = AddTradeMarkers(candleSnap, trade);
            LargestStake = new LargestStakeInfo
            {
                Trade = ApplySnapshotSpotValues(trade, candleWithPrices),
                Market = trade.Market,
                TickSnapshot = snapshot,
                CandleSnapshot = candleWithPrices
            };
        }
    }

    private bool IsLargestStakeTrade(TradeHistoryItem trade)
    {
        return LargestStake?.Trade.ContractId == trade.ContractId && trade.ContractId > 0;
    }

    private static TradeHistoryItem ApplySnapshotSpotValues(TradeHistoryItem trade, CandleSnapshot? snapshot)
    {
        if (snapshot?.IsEntryAnchored != true)
            return trade;

        return new TradeHistoryItem
        {
            Operacao = trade.Operacao,
            Estrategia = trade.Estrategia,
            Market = trade.Market,
            Tipo = trade.Tipo,
            ReferenceNumber = trade.ReferenceNumber,
            PurchaseTime = trade.PurchaseTime,
            Stake = trade.Stake,
            SellTime = trade.SellTime,
            EntrySpot = FormatSpot(snapshot.EntryPrice, trade.EntrySpot),
            ExitSpot = FormatSpot(snapshot.ExitPrice, trade.ExitSpot),
            ContractValue = trade.ContractValue,
            ProfitLoss = trade.ProfitLoss,
            ContractId = trade.ContractId
        };
    }

    private static string FormatSpot(decimal? value, string fallback)
    {
        return value.HasValue
            ? value.Value.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)
            : fallback;
    }

    private void UpdateLossStreak(TradeHistoryItem trade)
    {
        bool lost = !trade.ProfitLoss.HasValue || trade.ProfitLoss.Value <= 0;

        if (lost)
        {
            _currentLossStreak.Add(trade.Stake);
        }
        else
        {
            // Se acabou uma streak, salva se for a maior
            if (_currentLossStreak.Count > 0)
            {
                if (_currentLossStreak.Count > (LongestLossStreak?.Length ?? 0))
                {
                    _tickSnapshots.TryGetValue(trade.ContractId, out var tickSnap);
                    _candleSnapshots.TryGetValue(trade.ContractId, out var candleSnap);
                    var candleWithPrices = AddTradeMarkers(candleSnap, trade);
                    LongestLossStreak = new LossStreakInfo
                    {
                        Length = _currentLossStreak.Count,
                        Stakes = new List<decimal>(_currentLossStreak),
                        TotalLost = _currentLossStreak.Sum(),
                        TickSnapshot = tickSnap,
                        CandleSnapshot = candleWithPrices
                    };
                    _longestLossStreakContractId = trade.ContractId;
                }
                _currentLossStreak.Clear();
            }
        }

        // Atualiza a streak atual mesmo se não for a maior
        if (lost && _currentLossStreak.Count > (LongestLossStreak?.Length ?? 0))
        {
            _tickSnapshots.TryGetValue(trade.ContractId, out var tickSnap);
            _candleSnapshots.TryGetValue(trade.ContractId, out var candleSnap);
            var candleWithPrices = AddTradeMarkers(candleSnap, trade);
            LongestLossStreak = new LossStreakInfo
            {
                Length = _currentLossStreak.Count,
                Stakes = new List<decimal>(_currentLossStreak),
                TotalLost = _currentLossStreak.Sum(),
                TickSnapshot = tickSnap,
                CandleSnapshot = candleWithPrices
            };
            _longestLossStreakContractId = trade.ContractId;
        }
    }

    private void RefreshLossStreakSnapshot(TradeHistoryItem trade)
    {
        if (trade.ContractId != _longestLossStreakContractId || LongestLossStreak == null)
            return;

        _candleSnapshots.TryGetValue(trade.ContractId, out var candleSnap);
        LongestLossStreak = new LossStreakInfo
        {
            Length = LongestLossStreak.Length,
            Stakes = LongestLossStreak.Stakes,
            TotalLost = LongestLossStreak.TotalLost,
            TickSnapshot = LongestLossStreak.TickSnapshot,
            CandleSnapshot = AddTradeMarkers(candleSnap, trade)
        };
    }

    private static CandleSnapshot AddEntryMarkerIndex(CandleSnapshot snapshot, long entryEpoch)
    {
        return new CandleSnapshot
        {
            Candles = snapshot.Candles,
            Type = snapshot.Type,
            HighlightIndex = snapshot.HighlightIndex,
            EntryPrice = snapshot.EntryPrice,
            ExitPrice = snapshot.ExitPrice,
            EntryIndex = snapshot.EntryIndex ?? FindCandleIndex(snapshot.Candles, entryEpoch, null) ?? snapshot.HighlightIndex,
            ExitIndex = snapshot.ExitIndex,
            IsEntryAnchored = snapshot.IsEntryAnchored
        };
    }

    private (TickSnapshot? TickSnapshot, CandleSnapshot? CandleSnapshot) GetDrawdownSnapshot(DateTime occurredAt)
    {
        if (_lastCompletedTrade == null || !IsDrawdownTradeMatch(_lastCompletedTrade, occurredAt))
            return (_lastTickSnapshot, _lastCandleSnapshot);

        return GetMarkedTradeSnapshot(_lastCompletedTrade, _lastCandleSnapshot);
    }

    private void RefreshMaxDrawdownSnapshot(TradeHistoryItem trade)
    {
        if (MaxDrawdown == null || !IsDrawdownTradeMatch(trade, MaxDrawdown.OccurredAt))
            return;

        var snapshot = GetMarkedTradeSnapshot(trade, MaxDrawdown.CandleSnapshot);
        MaxDrawdown = new DrawdownInfo
        {
            DrawdownValue = MaxDrawdown.DrawdownValue,
            OccurredAt = MaxDrawdown.OccurredAt,
            PeakBalance = MaxDrawdown.PeakBalance,
            TroughBalance = MaxDrawdown.TroughBalance,
            TickSnapshot = snapshot.TickSnapshot ?? MaxDrawdown.TickSnapshot,
            CandleSnapshot = snapshot.CandleSnapshot ?? MaxDrawdown.CandleSnapshot
        };
    }

    private (TickSnapshot? TickSnapshot, CandleSnapshot? CandleSnapshot) GetMarkedTradeSnapshot(
        TradeHistoryItem trade, CandleSnapshot? fallback)
    {
        _tickSnapshots.TryGetValue(trade.ContractId, out var tickSnap);
        _candleSnapshots.TryGetValue(trade.ContractId, out var candleSnap);
        return (tickSnap, AddTradeMarkers(candleSnap ?? fallback, trade));
    }

    private static bool IsDrawdownTradeMatch(TradeHistoryItem trade, DateTime occurredAt)
    {
        if (!trade.ProfitLoss.HasValue || trade.ProfitLoss.Value > 0)
            return false;

        var referenceTime = trade.SellTime ?? trade.PurchaseTime;
        return Math.Abs((occurredAt - referenceTime).TotalSeconds) <= DrawdownTradeMatchSeconds;
    }

    private static CandleSnapshot? AddTradeMarkers(CandleSnapshot? snapshot, TradeHistoryItem trade)
    {
        snapshot ??= CreateFallbackSnapshot(trade);
        if (snapshot == null) return null;

        var entryEpoch = ToEpochSeconds(trade.PurchaseTime);
        var entryPrice = ParseDecimal(trade.EntrySpot);
        var exitPrice = ParseDecimal(trade.ExitSpot);
        if (snapshot.Type == ChartSnapshotType.TickCandles)
            return AddTickCandleTradeMarkers(snapshot, entryEpoch, entryPrice, exitPrice);

        int markerIndex = GetEntryMarkerIndex(snapshot, entryEpoch, entryPrice);
        var markerPrices = GetMarkerPrices(snapshot, markerIndex, entryPrice, exitPrice);

        return new CandleSnapshot
        {
            Candles = snapshot.Candles,
            Type = snapshot.Type,
            HighlightIndex = snapshot.HighlightIndex,
            EntryPrice = markerPrices.Entry,
            ExitPrice = markerPrices.Exit,
            EntryIndex = markerIndex,
            ExitIndex = markerIndex,
            IsEntryAnchored = snapshot.IsEntryAnchored
        };
    }

    private static CandleSnapshot? CreateFallbackSnapshot(TradeHistoryItem trade)
    {
        var entryPrice = ParseDecimal(trade.EntrySpot);
        var exitPrice = ParseDecimal(trade.ExitSpot);
        if (!entryPrice.HasValue || !exitPrice.HasValue)
            return null;

        long entryEpoch = ToEpochSeconds(trade.PurchaseTime) ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long exitEpoch = ToEpochSeconds(trade.SellTime) ?? entryEpoch + 1;
        if (exitEpoch <= entryEpoch)
            exitEpoch = entryEpoch + 1;

        var candles = new List<CandleData>
        {
            new()
            {
                Epoch = entryEpoch,
                Open = entryPrice.Value,
                High = Math.Max(entryPrice.Value, exitPrice.Value),
                Low = Math.Min(entryPrice.Value, exitPrice.Value),
                Close = exitPrice.Value
            },
            new()
            {
                Epoch = exitEpoch,
                Open = exitPrice.Value,
                High = exitPrice.Value,
                Low = exitPrice.Value,
                Close = exitPrice.Value
            }
        };

        return new CandleSnapshot
        {
            Candles = candles,
            Type = ChartSnapshotType.Candles,
            HighlightIndex = 0,
            EntryPrice = null,
            ExitPrice = null,
            EntryIndex = null,
            ExitIndex = null,
            IsEntryAnchored = false
        };
    }

    private static CandleSnapshot AddTickCandleTradeMarkers(
        CandleSnapshot snapshot, long? entryEpoch, decimal? entryPrice, decimal? exitPrice)
    {
        int markerIndex = GetEntryMarkerIndex(snapshot, entryEpoch, entryPrice);
        var markerPrices = GetMarkerPrices(snapshot, markerIndex, entryPrice, exitPrice);

        return new CandleSnapshot
        {
            Candles = snapshot.Candles,
            Type = snapshot.Type,
            HighlightIndex = snapshot.HighlightIndex,
            EntryPrice = markerPrices.Entry,
            ExitPrice = markerPrices.Exit,
            EntryIndex = markerIndex,
            ExitIndex = markerIndex,
            IsEntryAnchored = snapshot.IsEntryAnchored
        };
    }

    private static (decimal? Entry, decimal? Exit) GetMarkerPrices(
        CandleSnapshot snapshot, int markerIndex, decimal? entryPrice, decimal? exitPrice)
    {
        if (!snapshot.IsEntryAnchored || markerIndex < 0 || markerIndex >= snapshot.Candles.Count)
            return (entryPrice, exitPrice);

        var candle = snapshot.Candles[markerIndex];
        return (candle.Open, candle.Close);
    }

    private static int GetEntryMarkerIndex(CandleSnapshot snapshot, long? entryEpoch, decimal? entryPrice)
    {
        return snapshot.EntryIndex
            ?? FindCandleIndex(snapshot.Candles, entryEpoch, entryPrice)
            ?? snapshot.HighlightIndex;
    }

    private static int? FindCandleIndex(IReadOnlyList<CandleData> candles, long? epoch, decimal? price)
    {
        if (candles.Count == 0) return null;

        var candidates = FindEpochCandidates(candles, epoch);
        if (price.HasValue)
            return FindClosestPriceMatch(candles, price.Value, candidates);

        if (!epoch.HasValue) return null;

        for (int i = 0; i < candles.Count; i++)
        {
            long start = candles[i].Epoch;
            long end = i + 1 < candles.Count ? candles[i + 1].Epoch : long.MaxValue;
            if (epoch.Value >= start && epoch.Value < end)
                return i;
        }

        return null;
    }

    private static List<int> FindEpochCandidates(IReadOnlyList<CandleData> candles, long? epoch)
    {
        var result = new List<int>();
        if (!epoch.HasValue) return result;

        for (int i = 0; i < candles.Count; i++)
        {
            long start = candles[i].Epoch;
            long end = GetNextDistinctEpoch(candles, i);
            if (epoch.Value == start || (epoch.Value >= start && epoch.Value < end))
                result.Add(i);
        }

        return result;
    }

    private static long GetNextDistinctEpoch(IReadOnlyList<CandleData> candles, int index)
    {
        long current = candles[index].Epoch;
        for (int i = index + 1; i < candles.Count; i++)
            if (candles[i].Epoch > current)
                return candles[i].Epoch;

        return long.MaxValue;
    }

    private static int? FindClosestPriceMatch(IReadOnlyList<CandleData> candles, decimal price, List<int> candidates)
    {
        var scoped = candidates.Count > 0 ? candidates : Enumerable.Range(0, candles.Count);
        return scoped
            .OrderBy(i => GetPriceDistance(candles[i], price))
            .ThenBy(i => Math.Abs((double)(candles[i].Close - price)))
            .FirstOrDefault();
    }

    private static decimal GetPriceDistance(CandleData candle, decimal price)
    {
        if (price >= candle.Low && price <= candle.High)
            return 0;

        return price < candle.Low ? candle.Low - price : price - candle.High;
    }

    private static long? ToEpochSeconds(DateTime? value)
    {
        if (!value.HasValue) return null;

        var local = DateTime.SpecifyKind(value.Value, DateTimeKind.Local);
        return new DateTimeOffset(local).ToUnixTimeSeconds();
    }

    private static decimal? ParseDecimal(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (decimal.TryParse(value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var result))
            return result;
        return null;
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
