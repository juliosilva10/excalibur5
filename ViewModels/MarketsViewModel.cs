using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Excalibur5.Models;
using Excalibur5.Services;

namespace Excalibur5.ViewModels;

public partial class MarketsViewModel : ObservableObject, IDisposable
{
    private readonly ITickStreamService _tickService;
    private readonly IContractService _contractService;

    [ObservableProperty] private bool                _isMarketsVisible;
    [ObservableProperty] private MarketTabViewModel? _selectedTab;

    public ObservableCollection<MarketTabViewModel> Tabs { get; } = new();

    public MarketsViewModel(ITickStreamService tickService, IContractService contractService)
    {
        _tickService = tickService;
        _contractService = contractService;

        foreach (var market in MarketInfo.SyntheticMarkets)
            Tabs.Add(new MarketTabViewModel(market, tickService, contractService));
    }

    public void SetRecoverViewModel(RecoverViewModel recoverVm)
    {
        foreach (var tab in Tabs)
            tab.SetRecoverViewModel(recoverVm);
    }

    [RelayCommand]
    private void ToggleMarkets()
    {
        if (IsMarketsVisible) return;
        IsMarketsVisible = true;
    }

    public void Show()
    {
        IsMarketsVisible = true;
    }

    public async Task SelectTabAsync(MarketTabViewModel tab)
    {
        if (SelectedTab == tab) return;

        if (SelectedTab is not null)
        {
            SelectedTab.IsSelected = false;
            await SelectedTab.DeactivateAsync();
        }

        SelectedTab = tab;
        tab.IsSelected = true;

        if (IsMarketsVisible)
            await tab.ActivateAsync();
    }

    public async Task UnsubscribeAllAsync()
    {
        await _tickService.UnsubscribeAllAsync();
        foreach (var tab in Tabs)
        {
            tab.IsSubscribed = false;
            tab.IsSelected   = false;
        }
        SelectedTab = null;
    }

    public async Task ResubscribeActiveAsync()
    {
        if (SelectedTab is not null && IsMarketsVisible)
        {
            AppLogger.Info("Markets", $"Re-subscribing active tab: {SelectedTab.Symbol}");
            await SelectedTab.ForceDeactivateAsync();
            await SelectedTab.ActivateAsync();
        }
    }

    public void Dispose()
    {
        foreach (var tab in Tabs)
            tab.Dispose();
    }
}
