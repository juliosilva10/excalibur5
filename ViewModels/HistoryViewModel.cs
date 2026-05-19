using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Excalibur5.Models;
using Excalibur5.Services;

namespace Excalibur5.ViewModels;

public partial class HistoryViewModel : ObservableObject
{
    private const string Src = "History";
    private readonly IContractService _contractService;

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

        Application.Current.Dispatcher.Invoke(() =>
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

            TradeSettled?.Invoke(this, Trades[idx]);

            if (sellTime == null && update.ContractId > 0)
                _ = FetchSellTimeAsync(update.ContractId);
        });
    }

    [RelayCommand]
    private void ToggleHistory()
    {
        IsHistoryVisible = !IsHistoryVisible;
        if (IsHistoryVisible && Trades.Count == 0)
            _ = LoadFromApiAsync();
    }

    [RelayCommand]
    private async Task Refresh()
    {
        await LoadFromApiAsync();
    }

    public async Task LoadFromApiAsync()
    {
        if (IsLoading) return;
        IsLoading = true;

        try
        {
            var entries = await _contractService.GetProfitTableAsync(100);
            Application.Current.Dispatcher.Invoke(() =>
            {
                Trades.Clear();
                foreach (var e in entries)
                {
                    Trades.Add(new TradeHistoryItem
                    {
                        Operacao = "Manual",
                        Estrategia = "",
                        Tipo = FormatContractType(e.ContractType),
                        ReferenceNumber = e.ContractId.ToString(),
                        PurchaseTime = DateTimeOffset.FromUnixTimeSeconds(e.PurchaseTime).LocalDateTime,
                        Stake = e.BuyPrice,
                        SellTime = e.SellTime > 0 ? DateTimeOffset.FromUnixTimeSeconds(e.SellTime).LocalDateTime : null,
                        EntrySpot = e.EntrySpot,
                        ExitSpot = e.ExitSpot,
                        ContractValue = e.SellPrice > 0 ? e.SellPrice : null,
                        ProfitLoss = e.ProfitLoss,
                        ContractId = e.ContractId
                    });
                }
            });
            AppLogger.Info(Src, $"Loaded {entries.Count} trades from API");

            _ = FetchSpotsAsync();
        }
        catch (Exception ex)
        {
            AppLogger.Error(Src, $"Failed to load profit table: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task FetchSpotsAsync()
    {
        var items = Trades.Where(t => t.ContractId > 0).ToList();
        foreach (var item in items)
        {
            try
            {
                var status = await _contractService.GetContractStatusAsync(item.ContractId);
                if (status == null) continue;

                Application.Current.Dispatcher.Invoke(() =>
                {
                    var idx = Trades.IndexOf(item);
                    if (idx < 0) return;

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
                        SellTime = status.SellTime > 0
                            ? DateTimeOffset.FromUnixTimeSeconds(status.SellTime).LocalDateTime
                            : item.SellTime,
                        EntrySpot = !string.IsNullOrEmpty(status.EntrySpotRaw) ? status.EntrySpotRaw : item.EntrySpot,
                        ExitSpot = !string.IsNullOrEmpty(status.ExitSpotRaw) ? status.ExitSpotRaw : item.ExitSpot,
                        ContractValue = item.ContractValue,
                        ProfitLoss = item.ProfitLoss,
                        ContractId = item.ContractId
                    };
                });
            }
            catch { }
        }
    }

    public void AddBotTrade(BuyResponse buy, string contractType, string strategyName, string market)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            Trades.Insert(0, new TradeHistoryItem
            {
                Operacao = "Bot",
                Estrategia = strategyName,
                Market = market,
                Tipo = FormatContractType(contractType),
                ReferenceNumber = buy.ContractId.ToString(),
                PurchaseTime = DateTimeOffset.FromUnixTimeSeconds(buy.StartTime).LocalDateTime,
                Stake = buy.BuyPrice,
                ContractId = buy.ContractId
            });
        });
    }

    public void AddManualTrade(BuyResponse buy, string contractType, string market)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            Trades.Insert(0, new TradeHistoryItem
            {
                Operacao = "Manual",
                Estrategia = "",
                Market = market,
                Tipo = FormatContractType(contractType),
                ReferenceNumber = buy.ContractId.ToString(),
                PurchaseTime = DateTimeOffset.FromUnixTimeSeconds(buy.StartTime).LocalDateTime,
                Stake = buy.BuyPrice,
                ContractId = buy.ContractId
            });
        });
    }

    public void UpdateTradeResult(long contractId, decimal profit, long sellTime = 0)
    {
        Application.Current.Dispatcher.Invoke(() =>
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

                var alreadyFilled = Application.Current.Dispatcher.Invoke(() =>
                    Trades.FirstOrDefault(t => t.ContractId == contractId)?.SellTime != null);
                if (alreadyFilled) return;

                var status = await _contractService.GetContractStatusAsync(contractId);
                if (status == null || status.SellTime <= 0) continue;

                var sellTime = DateTimeOffset.FromUnixTimeSeconds(status.SellTime).LocalDateTime;

                Application.Current.Dispatcher.Invoke(() =>
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
                });
                return;
            }
            catch (Exception ex)
            {
                AppLogger.Warn(Src, $"FetchSellTime error for {contractId}: {ex.Message}");
            }
        }
    }

    private static string FormatContractType(string raw)
    {
        return raw switch
        {
            "VANILLALONGCALL" => "Vanillas Call",
            "VANILLALONGPUT" => "Vanillas Put",
            "CALL" => "Rise",
            "PUT" => "Fall",
            "CALLE" => "Higher",
            "PUTE" => "Lower",
            "MULTUP" => "Multiplier Up",
            "MULTDOWN" => "Multiplier Down",
            _ => raw
        };
    }
}
