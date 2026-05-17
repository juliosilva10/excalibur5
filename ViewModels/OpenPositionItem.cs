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

    public long DateStart { get; set; }
    public long DateExpiry { get; set; }
    public long EntryTickTime { get; set; }
    public int DurationSeconds { get; }
    public long CreatedAtLocal { get; }

    public OpenPositionItem(long contractId, string symbol, string displayName, string contractType, decimal buyPrice, long dateStart, long dateExpiry, int durationSeconds)
    {
        ContractId = contractId;
        Symbol = symbol;
        DisplayName = displayName;
        ContractTypeLabel = GetContractLabel(contractType);
        BuyPrice = buyPrice;
        DateStart = dateStart;
        DateExpiry = dateExpiry;
        DurationSeconds = durationSeconds;
        CreatedAtLocal = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        IsValidToSell = true;
        UpdateTimeProgress();
    }

    public void UpdateTimeProgress()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var total = (long)(DurationSeconds > 0 ? DurationSeconds : 120);
        var elapsed = now - CreatedAtLocal;
        var remaining = total - elapsed;

        if (remaining < 0) remaining = 0;

        TimeProgress = Math.Clamp((double)elapsed / total, 0.0, 1.0);
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

    private static string GetContractLabel(string contractType) => contractType switch
    {
        "CALL" or "CALLE" => "Rise",
        "PUT" or "PUTE" => "Fall",
        "VANILLALONGCALL" => "Call",
        "VANILLALONGPUT" => "Put",
        _ => contractType.Contains("CALL") ? "Call" : "Put"
    };
}
