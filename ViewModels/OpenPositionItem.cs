using CommunityToolkit.Mvvm.ComponentModel;

namespace Excalibur5.ViewModels;

public partial class OpenPositionItem : ObservableObject
{
    public long ContractId { get; }
    public string Symbol { get; }
    public string DisplayName { get; }
    public string ContractTypeLabel { get; }
    public decimal BuyPrice { get; }

    [ObservableProperty] private decimal _currentValue;
    [ObservableProperty] private decimal _entrySpot;
    [ObservableProperty] private string _entrySpotDisplay = "";
    [ObservableProperty] private decimal _profit;
    [ObservableProperty] private double _timeProgress;
    [ObservableProperty] private bool _isValidToSell;
    [ObservableProperty] private bool _isSelling;
    [ObservableProperty] private string _remainingTimeDisplay = "";
    [ObservableProperty] private bool _isNearExpiry;
    [ObservableProperty] private bool _isProfitable;

    public long DateStart { get; }
    public long DateExpiry { get; }

    public OpenPositionItem(long contractId, string symbol, string displayName, string contractType, decimal buyPrice, long dateStart, long dateExpiry)
    {
        ContractId = contractId;
        Symbol = symbol;
        DisplayName = displayName;
        ContractTypeLabel = contractType.Contains("CALL") ? "Call" : "Put";
        BuyPrice = buyPrice;
        DateStart = dateStart;
        DateExpiry = dateExpiry;
        IsValidToSell = true;
        UpdateTimeProgress();
    }

    public void UpdateTimeProgress()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var total = DateExpiry - DateStart;
        if (total <= 0)
        {
            TimeProgress = 1.0;
            RemainingTimeDisplay = "0s";
            IsNearExpiry = true;
            return;
        }
        var elapsed = now - DateStart;
        TimeProgress = Math.Clamp((double)elapsed / total, 0.0, 1.0);

        var remaining = DateExpiry - now;
        RemainingTimeDisplay = FormatRemaining(remaining);
        IsNearExpiry = remaining <= 5;
    }

    partial void OnProfitChanged(decimal value) => IsProfitable = value >= 0;

    private static string FormatRemaining(long seconds)
    {
        if (seconds <= 0) return "0s";
        if (seconds < 60) return $"{seconds}s";
        if (seconds < 3600) return $"{seconds / 60}m{seconds % 60}s";
        return $"{seconds / 3600}h{(seconds % 3600) / 60}m";
    }
}
