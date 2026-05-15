using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Excalibur5.Models;
using Excalibur5.Services;

namespace Excalibur5.ViewModels;

public partial class OpenPositionsViewModel : ObservableObject, IDisposable
{
    private const string Src = "OpenPositions";
    private readonly IContractService _contractService;
    private readonly DispatcherTimer _timer;

    public ObservableCollection<OpenPositionItem> Positions { get; } = new();

    [ObservableProperty] private bool _hasPositions;

    public OpenPositionsViewModel(IContractService contractService)
    {
        _contractService = contractService;
        _contractService.OpenContractUpdated += OnOpenContractUpdated;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTimerTick;
        _timer.Start();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var expired = new List<OpenPositionItem>();

        foreach (var pos in Positions)
        {
            pos.UpdateTimeProgress();
            if (pos.DateExpiry > 0 && pos.DateExpiry + 10 <= now)
                expired.Add(pos);
        }

        foreach (var pos in expired)
            RemovePosition(pos.ContractId);
    }

    private void OnOpenContractUpdated(object? sender, OpenContractUpdate update)
    {
        Application.Current?.Dispatcher?.InvokeAsync(() =>
        {
            var item = FindPosition(update.ContractId);
            if (item == null) return;

            item.CurrentValue = update.BidPrice;
            item.Profit = update.Profit;
            item.IsValidToSell = update.IsValidToSell;

            if (update.EntrySpot > 0)
            {
                item.EntrySpot = update.EntrySpot;
                if (!string.IsNullOrEmpty(update.EntrySpotRaw))
                    item.EntrySpotDisplay = update.EntrySpotRaw;
            }

            if (update.IsExpired || update.IsSold || update.Status == "sold" || update.Status == "lost" || update.Status == "won")
            {
                RemovePosition(update.ContractId);
            }
        });
    }

    public async Task AddPositionAsync(BuyResponse buyResult, string symbol, string displayName, string contractType, long dateExpiry)
    {
        if (buyResult.ContractId == 0) return;

        var item = new OpenPositionItem(
            buyResult.ContractId,
            symbol,
            displayName,
            contractType,
            buyResult.BuyPrice,
            buyResult.StartTime,
            dateExpiry);

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            Positions.Add(item);
            HasPositions = Positions.Count > 0;
        });

        try
        {
            await _contractService.SubscribeOpenContractAsync(buyResult.ContractId);
        }
        catch (Exception ex)
        {
            AppLogger.Warn(Src, $"Subscribe open contract failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task SellAsync(long contractId)
    {
        var item = FindPosition(contractId);
        if (item == null || item.IsSelling) return;

        item.IsSelling = true;

        try
        {
            var result = await _contractService.SellContractAsync(contractId);
            if (result.Success)
            {
                AppLogger.Info(Src, $"Sold contract {contractId} for {result.SoldFor}");
                RemovePosition(contractId);
            }
            else
            {
                AppLogger.Warn(Src, $"Sell failed: {result.Error}");
                item.IsSelling = false;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn(Src, $"Sell error: {ex.Message}");
            item.IsSelling = false;
        }
    }

    private void RemovePosition(long contractId)
    {
        var item = FindPosition(contractId);
        if (item == null) return;

        Positions.Remove(item);
        HasPositions = Positions.Count > 0;

        _ = Task.Run(async () =>
        {
            try { await _contractService.UnsubscribeOpenContractAsync(contractId); }
            catch { /* best effort */ }
        });
    }

    private OpenPositionItem? FindPosition(long contractId)
    {
        foreach (var p in Positions)
            if (p.ContractId == contractId)
                return p;
        return null;
    }

    public void Dispose()
    {
        _timer.Stop();
        _contractService.OpenContractUpdated -= OnOpenContractUpdated;
    }
}
