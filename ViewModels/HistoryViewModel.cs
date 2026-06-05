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
        if (!update.IsExpired && !update.IsSold && update.Status is not ("sold" or "won" or "lost")) return;

        Application.Current?.Dispatcher?.InvokeAsync(() =>
        {
            var item = Trades.FirstOrDefault(t => t.ContractId == update.ContractId);
            if (item == null) return;

            var idx = Trades.IndexOf(item);
            if (idx < 0) return;

            var exitSpot = !string.IsNullOrEmpty(update.ExitSpotRaw) ? update.ExitSpotRaw
                         : update.CurrentSpot > 0 ? update.CurrentSpot.ToString(CultureInfo.InvariantCulture)
                         : item.ExitSpot;

            DateTime? sellTime = update.SellTime > 0
                ? DateTimeOffset.FromUnixTimeSeconds(update.SellTime).LocalDateTime
                : null;

            var purchaseTime = update.EntryTickTime > 0
                ? DateTimeOffset.FromUnixTimeSeconds(update.EntryTickTime).LocalDateTime
                : item.PurchaseTime;

            Trades[idx] = new TradeHistoryItem
            {
                Operacao = item.Operacao,
                Estrategia = item.Estrategia,
                Market = item.Market,
                Tipo = item.Tipo,
                ReferenceNumber = item.ReferenceNumber,
                PurchaseTime = purchaseTime,
                Stake = item.Stake,
                SellTime = sellTime,
                EntrySpot = update.EntrySpotRaw.Length > 0 ? update.EntrySpotRaw : item.EntrySpot,
                ExitSpot = exitSpot,
                ContractValue = update.BidPrice,
                ProfitLoss = update.Profit,
                ContractId = item.ContractId
            };

            NotifyTradeSettled(Trades[idx]);

            if (sellTime == null && update.ContractId > 0)
                _ = FetchSellTimeAsync(update.ContractId);
        });
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

            if (resolvedSellTime == null && contractId > 0)
                _ = FetchSellTimeAsync(contractId);
        });
    }

    private async Task FetchSellTimeAsync(long contractId)
    {
        int[] delays = [500, 1500, 3000];
        foreach (var delay in delays)
        {
            try
            {
                await Task.Delay(delay);

                var alreadyFilled = Application.Current?.Dispatcher?.Invoke(() =>
                    Trades.FirstOrDefault(t => t.ContractId == contractId)?.SellTime != null);
                if (alreadyFilled == true) return;

                var status = await _contractService.GetContractStatusAsync(contractId);
                if (status == null || status.SellTime <= 0) continue;

                var sellTime = DateTimeOffset.FromUnixTimeSeconds(status.SellTime).LocalDateTime;

                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    var item = Trades.FirstOrDefault(t => t.ContractId == contractId);
                    if (item == null) return;

                    var idx = Trades.IndexOf(item);
                    if (idx < 0) return;

                    if (item.SellTime != null) return;

                    var entryTime = status.EntryTickTime > 0
                        ? DateTimeOffset.FromUnixTimeSeconds(status.EntryTickTime).LocalDateTime
                        : item.PurchaseTime;

                    Trades[idx] = new TradeHistoryItem
                    {
                        Operacao = item.Operacao,
                        Estrategia = item.Estrategia,
                        Market = item.Market,
                        Tipo = item.Tipo,
                        ReferenceNumber = item.ReferenceNumber,
                        PurchaseTime = entryTime,
                        Stake = item.Stake,
                        SellTime = sellTime,
                        EntrySpot = !string.IsNullOrEmpty(status.EntrySpotRaw) ? status.EntrySpotRaw : item.EntrySpot,
                        ExitSpot = !string.IsNullOrEmpty(status.ExitSpotRaw) ? status.ExitSpotRaw : item.ExitSpot,
                        ContractValue = item.ContractValue,
                        ProfitLoss = item.ProfitLoss,
                        ContractId = item.ContractId
                    };

                    NotifyTradeSettled(Trades[idx]);
                });
                return;
            }
            catch (Exception ex)
            {
                AppLogger.Warn(Src, $"FetchSellTime error for {contractId}: {ex.Message}");
            }
        }
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
