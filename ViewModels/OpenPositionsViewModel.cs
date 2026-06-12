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
    private readonly int _pipSize;

    public ObservableCollection<OpenPositionItem> Positions { get; } = new();

    [ObservableProperty] private bool _hasPositions;

    public OpenPositionsViewModel(IContractService contractService, int pipSize)
    {
        _contractService = contractService;
        _pipSize = pipSize;
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
            var elapsed = now - pos.CreatedAtLocal;
            if (pos.DurationSeconds > 0 && elapsed >= pos.DurationSeconds + 10)
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

            if (update.DateStart > 0 && update.DateExpiry > 0 && update.DateExpiry > update.DateStart)
            {
                item.DateStart = update.DateStart;
                item.DateExpiry = update.DateExpiry;
            }

            if (update.EntryTickTime > 0)
                item.EntryTickTime = update.EntryTickTime;

            if (update.EntrySpot > 0)
            {
                item.EntrySpot = update.EntrySpot;
                item.EntrySpotDisplay = MarketPriceFormatter.Format(
                    update.EntrySpot,
                    _pipSize);
            }

            if (update.IsExpired || update.IsSold || update.Status == "sold" || update.Status == "lost" || update.Status == "won")
            {
                RemovePosition(update.ContractId);
            }
        });
    }

    public async Task AddPositionAsync(BuyResponse buyResult, string symbol, string displayName, string contractType, long dateExpiry, int durationSeconds)
    {
        if (buyResult.ContractId == 0) return;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var startTime = buyResult.StartTime > 0 && buyResult.StartTime <= now
            ? buyResult.StartTime
            : now;

        var item = new OpenPositionItem(
            buyResult.ContractId,
            symbol,
            displayName,
            contractType,
            buyResult.BuyPrice,
            startTime,
            dateExpiry,
            durationSeconds);

        await (Application.Current?.Dispatcher?.InvokeAsync(() =>
        {
            Positions.Add(item);
            HasPositions = Positions.Count > 0;
        })?.Task ?? Task.CompletedTask);

        try
        {
            await _contractService.SubscribeOpenContractAsync(buyResult.ContractId);
        }
        catch (Exception ex)
        {
            AppLogger.Warn(Src, $"Subscribe open contract failed: {ex.Message}");
        }
    }

    public Task AddVirtualPositionAsync(
        long tradeId,
        string symbol,
        string displayName,
        string contractType,
        decimal stake,
        decimal entrySpot,
        int durationSeconds)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var item = new OpenPositionItem(
            tradeId,
            symbol,
            displayName,
            contractType,
            stake,
            now,
            now + durationSeconds,
            durationSeconds,
            isVirtual: true)
        {
            EntrySpot = entrySpot,
            EntrySpotDisplay = MarketPriceFormatter.Format(entrySpot, _pipSize),
            CurrentValue = stake
        };

        return AddItemAsync(item);
    }

    public void UpdateVirtualPosition(long tradeId, decimal currentSpot)
    {
        var item = FindPosition(tradeId);
        if (item == null || !item.IsVirtual) return;

        var won = item.IsCallDirection
            ? currentSpot > item.EntrySpot
            : currentSpot < item.EntrySpot;
        item.Profit = won ? item.BuyPrice * 0.5m : -item.BuyPrice;
        item.CurrentValue = Math.Max(0m, item.BuyPrice + item.Profit);
    }

    public void CompleteVirtualPosition(long tradeId)
    {
        var item = FindPosition(tradeId);
        if (item?.IsVirtual == true)
            RemovePosition(tradeId);
    }

    private async Task AddItemAsync(OpenPositionItem item)
    {
        await (Application.Current?.Dispatcher?.InvokeAsync(() =>
        {
            Positions.Add(item);
            HasPositions = Positions.Count > 0;
        })?.Task ?? Task.CompletedTask);
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

        if (item.IsVirtual) return;

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
