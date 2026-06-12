using Excalibur5.Config;
using Excalibur5.Services.Strategy.Virtual;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Excalibur5.ViewModels;

public partial class VirtualViewModel : ObservableObject, IVirtualEntrySettingsProvider
{
    private bool _restoringState;

    [ObservableProperty] private bool _isVirtualVisible;
    [ObservableProperty] private string _targetSequence = "LL";
    [ObservableProperty] private string _toleranceText = string.Empty;

    public string SequenceSummary => BuildSequenceSummary(TargetSequence);

    public VirtualViewModel()
    {
        RestoreState();
    }

    public event EventHandler? SettingsChanged;

    [RelayCommand]
    private void ToggleVirtual()
    {
        IsVirtualVisible = !IsVirtualVisible;
    }

    [RelayCommand]
    private void RecordWin()
    {
        TargetSequence += "W";
    }

    [RelayCommand]
    private void RecordLoss()
    {
        TargetSequence += "L";
    }

    [RelayCommand]
    private void DeleteLastEntry()
    {
        if (TargetSequence.Length > 0)
            TargetSequence = TargetSequence[..^1];
    }

    [RelayCommand]
    private void ClearConfiguration()
    {
        TargetSequence = string.Empty;
        ToleranceText = string.Empty;
    }

    public VirtualEntrySettings GetSettings()
    {
        var tolerance = int.TryParse(ToleranceText, out var parsed) && parsed > 0
            ? parsed
            : 0;
        return new VirtualEntrySettings(TargetSequence, tolerance);
    }

    private static string BuildSequenceSummary(string sequence)
    {
        if (string.IsNullOrEmpty(sequence)) return string.Empty;

        var groups = new List<string>();
        var groupStart = 0;

        for (var index = 1; index <= sequence.Length; index++)
        {
            if (index < sequence.Length &&
                char.ToUpperInvariant(sequence[index]) == char.ToUpperInvariant(sequence[groupStart])) continue;

            groups.Add($"{index - groupStart}{char.ToUpperInvariant(sequence[groupStart])}");
            groupStart = index;
        }

        return $"({string.Join(' ', groups)})";
    }

    partial void OnTargetSequenceChanged(string value)
    {
        OnPropertyChanged(nameof(SequenceSummary));
        SaveState();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnToleranceTextChanged(string value)
    {
        SaveState();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RestoreState()
    {
        _restoringState = true;
        var state = VirtualEntryStateStore.Load();
        TargetSequence = NormalizeSequence(state.TargetSequence);
        ToleranceText = NormalizeTolerance(state.ToleranceText);
        _restoringState = false;
    }

    private void SaveState()
    {
        if (_restoringState) return;

        VirtualEntryStateStore.Save(new VirtualEntryState
        {
            TargetSequence = TargetSequence,
            ToleranceText = ToleranceText
        });
    }

    private static string NormalizeSequence(string value)
    {
        return new string((value ?? string.Empty)
            .Where(character => character is 'W' or 'L' or 'w' or 'l')
            .Select(char.ToUpperInvariant)
            .ToArray());
    }

    private static string NormalizeTolerance(string value)
    {
        return int.TryParse(value, out var tolerance) && tolerance > 0
            ? tolerance.ToString()
            : string.Empty;
    }
}
