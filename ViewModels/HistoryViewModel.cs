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

            Trades[idx] = new TradeHistoryItem
            {
                Operacao = item.Operacao,
                Estrategia = item.Estrategia,
                Market = item.Market,
                Tipo = item.Tipo,
                ReferenceNumber = item.ReferenceNumber,
                PurchaseTime = item.PurchaseTime,
                Stake = item.Stake,
                SellTime = update.DateExpiry > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(update.DateExpiry).LocalDateTime
                    : DateTime.Now,
                EntrySpot = update.EntrySpotRaw.Length > 0 ? update.EntrySpotRaw : item.EntrySpot,
                ExitSpot = exitSpot,
                ContractValue = update.BidPrice,
                ProfitLoss = update.Profit,
                ContractId = item.ContractId
            };

            TradeSettled?.Invoke(this, Trades[idx]);
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
        var items = Trades.Where(t => t.ContractId > 0 && string.IsNullOrEmpty(t.EntrySpot)).ToList();
        foreach (var item in items)
        {
            try
            {
                var (entry, exit) = await _contractService.GetContractSpotsAsync(item.ContractId);
                if (!string.IsNullOrEmpty(entry) || !string.IsNullOrEmpty(exit))
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        var idx = Trades.IndexOf(item);
                        if (idx >= 0)
                        {
                            item.EntrySpot = entry;
                            item.ExitSpot = exit;
                            Trades[idx] = item;
                        }
                    });
                }
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

    public void UpdateTradeResult(long contractId, decimal profit)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var item = Trades.FirstOrDefault(t => t.ContractId == contractId);
            if (item == null) return;

            var idx = Trades.IndexOf(item);
            if (idx < 0) return;

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
                ExitSpot = item.ExitSpot,
                ContractValue = item.Stake + profit,
                ProfitLoss = profit,
                ContractId = item.ContractId
            };
        });
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
