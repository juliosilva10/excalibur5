using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Excalibur5.Config;

namespace Excalibur5.ViewModels;

public partial class RecoverViewModel : ObservableObject, IDisposable
{
    private bool _restoringState;

    [ObservableProperty] private bool _isRecoverVisible;
    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private string _selectedMode = "Martingale";

    // Martingale fields
    [ObservableProperty] private string _stakeText = "0.35";
    [ObservableProperty] private string _factorText = "2.00";
    [ObservableProperty] private string _maxLevelText = "3";

    // Deficit Recovery fields
    [ObservableProperty] private string _deficitMaxStakeText = "50";
    [ObservableProperty] private string _deficitRecoveryTradesText = "1";

    public List<string> RecoverModes { get; } = ["Martingale", "Deficit Recovery"];
    public bool IsMartingaleMode => SelectedMode == "Martingale";
    public bool IsDeficitMode => SelectedMode == "Deficit Recovery";

    public decimal BaseStake => decimal.TryParse(StakeText, System.Globalization.NumberStyles.Number,
        System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0.35m;

    public decimal Factor => decimal.TryParse(FactorText, System.Globalization.NumberStyles.Number,
        System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 2.00m;

    public int MaxLevel => int.TryParse(MaxLevelText, out var v) ? v : 3;

    public decimal DeficitMaxStake => decimal.TryParse(DeficitMaxStakeText, System.Globalization.NumberStyles.Number,
        System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 50m;

    public int DeficitRecoveryTrades => int.TryParse(DeficitRecoveryTradesText, out var v) && v >= 1 ? v : 1;

    public RecoverViewModel()
    {
        RestoreState();
    }

    [RelayCommand]
    private void ToggleRecover()
    {
        IsRecoverVisible = !IsRecoverVisible;
    }

    public decimal CalculateStake(int currentLevel)
    {
        if (currentLevel <= 0 || currentLevel > MaxLevel)
            return BaseStake;

        var stake = BaseStake;
        for (int i = 0; i < currentLevel; i++)
            stake *= Factor;
        return Math.Round(stake, 2);
    }

    private void RestoreState()
    {
        _restoringState = true;
        var s = RecoverStateStore.Load();
        SelectedMode = s.SelectedMode;
        StakeText = s.StakeText;
        FactorText = s.FactorText;
        MaxLevelText = s.MaxLevelText;
        DeficitMaxStakeText = s.DeficitMaxStakeText;
        DeficitRecoveryTradesText = s.DeficitRecoveryTradesText;
        IsEnabled = s.IsEnabled;
        _restoringState = false;
    }

    private void SaveState()
    {
        if (_restoringState) return;
        RecoverStateStore.Save(new RecoverState
        {
            SelectedMode = SelectedMode,
            StakeText = StakeText,
            FactorText = FactorText,
            MaxLevelText = MaxLevelText,
            DeficitMaxStakeText = DeficitMaxStakeText,
            DeficitRecoveryTradesText = DeficitRecoveryTradesText,
            IsEnabled = IsEnabled
        });
    }

    partial void OnSelectedModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsMartingaleMode));
        OnPropertyChanged(nameof(IsDeficitMode));
        SaveState();
    }

    partial void OnIsEnabledChanged(bool value) => SaveState();
    partial void OnStakeTextChanged(string value) => SaveState();
    partial void OnFactorTextChanged(string value) => SaveState();
    partial void OnMaxLevelTextChanged(string value) => SaveState();
    partial void OnDeficitMaxStakeTextChanged(string value) => SaveState();
    partial void OnDeficitRecoveryTradesTextChanged(string value) => SaveState();

    public void Dispose() => SaveState();
}
