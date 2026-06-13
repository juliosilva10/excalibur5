using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Excalibur5.Models;
using Excalibur5.Services;

namespace Excalibur5.ViewModels;

public partial class HistoryViewModel : ObservableObject, IDisposable
{
    private const string Src = "History";
    private const int SettlementRefreshAttempts = 90;
    private const int SettlementRefreshDelayMs = 2000;
    private static readonly int[] InitialStatusRefreshDelays = [250, 1000, 2500, 5000];

    private readonly IContractService _contractService;
    private readonly Dictionary<long, string> _settledNotifications = new();

    [ObservableProperty] private bool _isHistoryVisible;
    [ObservableProperty] private bool _isLoading;

    public event EventHandler<TradeHistoryItem>? TradeSettled;

    public ObservableCollection<TradeHistoryItem> Trades { get; } = new();

    public HistoryViewModel(IContractService contractService)
    {
        _contractService = contractService;
        _contractService.OpenContractUpdated += OnOpenContractUpdated;
    }

    private void OnOpenContractUpdated(object? sender, OpenContractUpdate update)
    {
        if (!IsSettled(update)) return;

        _ = ApplyContractStatusAsync(update, notifySettled: true);
        if (update.ContractId > 0)
            _ = RefreshContractStatusAsync(update.ContractId, requireSettlement: true);
    }

    [RelayCommand]
    private void ToggleHistory()
    {
        IsHistoryVisible = true;
    }

    [RelayCommand]
    private void Refresh()
    {
        AppLogger.Info(Src, "History refresh ignored: showing current bot session only");
    }

    public void ResetSession()
    {
        IsLoading = false;
        _settledNotifications.Clear();
        Trades.Clear();
    }

    public void AddBotTrade(BuyResponse buy, string contractType, string strategyName, string market)
    {
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            Trades.Insert(0, new TradeHistoryItem
            {
                Operacao = "Bot",
                Estrategia = strategyName,
                Market = market,
                Tipo = ContractTypeFormatter.ToDisplayLabel(contractType),
                ReferenceNumber = buy.ContractId.ToString(),
                PurchaseTime = DateTimeOffset.FromUnixTimeSeconds(buy.StartTime).LocalDateTime,
                Stake = buy.BuyPrice,
                ContractId = buy.ContractId
            });
        });

        _ = RefreshContractStatusAsync(buy.ContractId, requireSettlement: true);
    }

    public void AddManualTrade(BuyResponse buy, string contractType, string market)
    {
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            Trades.Insert(0, new TradeHistoryItem
            {
                Operacao = "Manual",
                Estrategia = "",
                Market = market,
                Tipo = ContractTypeFormatter.ToDisplayLabel(contractType),
                ReferenceNumber = buy.ContractId.ToString(),
                PurchaseTime = DateTimeOffset.FromUnixTimeSeconds(buy.StartTime).LocalDateTime,
                Stake = buy.BuyPrice,
                ContractId = buy.ContractId
            });
        });

        _ = RefreshContractStatusAsync(buy.ContractId, requireSettlement: true);
    }

    public void AddVirtualTrade(
        long tradeId,
        string operacao,
        string strategyName,
        string market,
        string contractType,
        decimal stake,
        decimal entrySpot)
    {
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            Trades.Insert(0, new TradeHistoryItem
            {
                Operacao = operacao,
                Estrategia = strategyName,
                Market = market,
                Tipo = ContractTypeFormatter.ToDisplayLabel(contractType),
                ReferenceNumber = "Virtual",
                PurchaseTime = DateTime.Now,
                Stake = stake,
                EntrySpot = FormatSpot(entrySpot),
                ContractId = tradeId
            });
        });
    }

    public void UpdateVirtualTradeResult(long tradeId, bool won, decimal exitSpot, decimal winProfit)
    {
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            var item = Trades.FirstOrDefault(t => t.ContractId == tradeId);
            if (item == null) return;

            var idx = Trades.IndexOf(item);
            if (idx < 0) return;

            var profit = won ? winProfit : -item.Stake;

            Trades[idx] = new TradeHistoryItem
            {
                Operacao = item.Operacao,
                Estrategia = item.Estrategia,
                Market = item.Market,
                Tipo = item.Tipo,
                ReferenceNumber = item.ReferenceNumber,
                PurchaseTime = item.PurchaseTime,
                Stake = item.Stake,
                SellTime = DateTime.Now,
                EntrySpot = item.EntrySpot,
                ExitSpot = FormatSpot(exitSpot),
                ContractValue = Math.Max(0m, item.Stake + profit),
                ProfitLoss = profit,
                ContractId = item.ContractId
            };
        });
    }

    public void UpdateTradeResult(long contractId, decimal profit, long sellTime = 0)
    {
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            var item = Trades.FirstOrDefault(t => t.ContractId == contractId);
            if (item == null) return;

            var idx = Trades.IndexOf(item);
            if (idx < 0) return;

            DateTime? resolvedSellTime = sellTime > 0
                ? DateTimeOffset.FromUnixTimeSeconds(sellTime).LocalDateTime
                : item.SellTime;

            Trades[idx] = new TradeHistoryItem
            {
                Operacao = item.Operacao,
                Estrategia = item.Estrategia,
                Market = item.Market,
                Tipo = item.Tipo,
                ReferenceNumber = item.ReferenceNumber,
                PurchaseTime = item.PurchaseTime,
                Stake = item.Stake,
                SellTime = resolvedSellTime,
                EntrySpot = item.EntrySpot,
                ExitSpot = item.ExitSpot,
                ContractValue = item.Stake + profit,
                ProfitLoss = profit,
                ContractId = item.ContractId
            };

            NotifyTradeSettled(Trades[idx]);

            if (contractId > 0)
                _ = RefreshContractStatusAsync(contractId, requireSettlement: true);
        });
    }

    private async Task RefreshContractStatusAsync(long contractId, bool requireSettlement)
    {
        if (contractId <= 0) return;

        foreach (var delay in GetStatusRefreshDelays(requireSettlement))
        {
            try
            {
                await Task.Delay(delay);

                if (IsTradeComplete(contractId, requireSettlement)) return;

                var status = await _contractService.GetContractStatusAsync(contractId);
                if (status == null) continue;

                var settled = IsSettled(status);
                await ApplyContractStatusAsync(status, notifySettled: requireSettlement && settled);
                if (IsTradeComplete(contractId, requireSettlement)) return;
            }
            catch (Exception ex)
            {
                AppLogger.Warn(Src, $"RefreshContractStatus error for {contractId}: {ex.Message}");
            }
        }
    }

    private static IEnumerable<int> GetStatusRefreshDelays(bool requireSettlement)
    {
        foreach (var delay in InitialStatusRefreshDelays)
            yield return delay;

        if (!requireSettlement) yield break;

        for (var i = 0; i < SettlementRefreshAttempts; i++)
            yield return SettlementRefreshDelayMs;
    }

    private Task ApplyContractStatusAsync(OpenContractUpdate status, bool notifySettled)
    {
        return Application.Current?.Dispatcher?.InvokeAsync(() =>
        {
            var item = Trades.FirstOrDefault(t => t.ContractId == status.ContractId);
            if (item == null) return;

            var idx = Trades.IndexOf(item);
            if (idx < 0) return;

            var updated = MergeContractStatus(item, status);
            Trades[idx] = updated;

            if (notifySettled && IsSettled(status))
                NotifyTradeSettled(updated);
        }).Task ?? Task.CompletedTask;
    }

    private bool IsTradeComplete(long contractId, bool requireSettlement)
    {
        return Application.Current?.Dispatcher?.Invoke(() =>
        {
            var trade = Trades.FirstOrDefault(t => t.ContractId == contractId);
            return trade == null || !NeedsStatusRefresh(trade, requireSettlement);
        }) == true;
    }

    private static TradeHistoryItem MergeContractStatus(TradeHistoryItem item, OpenContractUpdate status)
    {
        var settled = IsSettled(status);
        return new TradeHistoryItem
        {
            Operacao = item.Operacao,
            Estrategia = item.Estrategia,
            Market = item.Market,
            Tipo = item.Tipo,
            ReferenceNumber = item.ReferenceNumber,
            PurchaseTime = ResolvePurchaseTime(item, status),
            Stake = item.Stake,
            SellTime = ToLocalTime(status.SellTime) ?? item.SellTime,
            EntrySpot = FirstText(status.EntrySpotRaw, FormatSpot(status.EntrySpot), item.EntrySpot),
            ExitSpot = ResolveExitSpot(item, status, settled),
            ContractValue = ResolveContractValue(item, status, settled),
            ProfitLoss = settled ? status.Profit : item.ProfitLoss,
            ContractId = item.ContractId
        };
    }

    private static DateTime ResolvePurchaseTime(TradeHistoryItem item, OpenContractUpdate status)
    {
        return ToLocalTime(status.EntryTickTime)
            ?? ToLocalTime(status.DateStart)
            ?? item.PurchaseTime;
    }

    private static string ResolveExitSpot(TradeHistoryItem item, OpenContractUpdate status, bool settled)
    {
        if (!settled) return item.ExitSpot;

        return FirstText(
            status.ExitSpotRaw,
            FormatSpot(status.CurrentSpot),
            item.ExitSpot);
    }

    private static decimal? ResolveContractValue(
        TradeHistoryItem item,
        OpenContractUpdate status,
        bool settled)
    {
        if (status.BidPrice > 0)
            return status.BidPrice;

        if (settled)
            return Math.Max(0m, GetBuyPrice(item, status) + status.Profit);

        return item.ContractValue;
    }

    private static decimal GetBuyPrice(TradeHistoryItem item, OpenContractUpdate status)
    {
        return status.BuyPrice > 0 ? status.BuyPrice : item.Stake;
    }

    private static bool NeedsStatusRefresh(TradeHistoryItem trade, bool requireSettlement)
    {
        if (string.IsNullOrWhiteSpace(trade.EntrySpot)) return true;
        if (!requireSettlement) return false;

        return trade.SellTime == null
            || string.IsNullOrWhiteSpace(trade.ExitSpot)
            || !trade.ContractValue.HasValue
            || !trade.ProfitLoss.HasValue;
    }

    private static bool IsSettled(OpenContractUpdate update)
    {
        return update.IsExpired || update.IsSold || update.Status is "sold" or "won" or "lost";
    }

    private static DateTime? ToLocalTime(long epoch)
    {
        return epoch > 0 ? DateTimeOffset.FromUnixTimeSeconds(epoch).LocalDateTime : null;
    }

    private static string FormatSpot(decimal spot)
    {
        return spot > 0 ? spot.ToString(CultureInfo.InvariantCulture) : string.Empty;
    }

    private static string FirstText(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    private void NotifyTradeSettled(TradeHistoryItem trade)
    {
        if (AlreadyNotified(trade)) return;

        AppLogger.Info(Src, $"Trade settled: contract={trade.ContractId}, entry={FormatTime(trade.PurchaseTime)}, exit={FormatTime(trade.SellTime)}, candleSeconds={GetTradeSeconds(trade)}");
        TradeSettled?.Invoke(this, trade);
    }

    private bool AlreadyNotified(TradeHistoryItem trade)
    {
        if (trade.ContractId <= 0) return false;

        var signature = $"{trade.SellTime?.Ticks ?? 0}|{trade.ProfitLoss}|{trade.ExitSpot}";
        if (_settledNotifications.TryGetValue(trade.ContractId, out var previous) && previous == signature)
            return true;

        _settledNotifications[trade.ContractId] = signature;
        return false;
    }

    private static string FormatTime(DateTime? time)
    {
        return time?.ToString("HH:mm:ss", CultureInfo.InvariantCulture) ?? "-";
    }

    private static int GetTradeSeconds(TradeHistoryItem trade)
    {
        if (trade.SellTime == null) return 0;
        return Math.Max(0, (int)Math.Round((trade.SellTime.Value - trade.PurchaseTime).TotalSeconds));
    }

    public void Dispose()
    {
        _contractService.OpenContractUpdated -= OnOpenContractUpdated;
    }

}
