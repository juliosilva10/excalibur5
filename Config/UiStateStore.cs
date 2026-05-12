using System.IO;
using System.Text.Json;

namespace Excalibur5.Config;

public static class UiStateStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Excalibur5", "uistate.json");

    public static void Save(string activePanel, string? selectedMarket, string? durationUnit = null, string? durationText = null, string? stakeText = null, bool? useDuration = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var existing = Load();
        var state = new UiState
        {
            ActivePanel = activePanel,
            SelectedMarket = selectedMarket,
            DurationUnit = durationUnit ?? existing.DurationUnit,
            DurationText = durationText ?? existing.DurationText,
            StakeText = stakeText ?? existing.StakeText,
            UseDuration = useDuration ?? existing.UseDuration
        };
        File.WriteAllText(FilePath, JsonSerializer.Serialize(state));
    }

    public static UiState Load()
    {
        if (!File.Exists(FilePath)) return new UiState();
        try
        {
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<UiState>(json) ?? new UiState();
        }
        catch
        {
            return new UiState();
        }
    }
}

public class UiState
{
    public string ActivePanel { get; set; } = "";
    public string? SelectedMarket { get; set; }
    public string? DurationUnit { get; set; }
    public string? DurationText { get; set; }
    public string? StakeText { get; set; }
    public bool? UseDuration { get; set; }
}
