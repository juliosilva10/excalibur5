using System.IO;
using System.Text.Json;

namespace Excalibur5.Config;

public static class VirtualEntryStateStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Excalibur5", "virtual-entry-state.json");

    public static void Save(VirtualEntryState state)
    {
        AtomicFile.WriteAllText(FilePath, JsonSerializer.Serialize(state));
    }

    public static VirtualEntryState Load()
    {
        if (!File.Exists(FilePath)) return new VirtualEntryState();

        try
        {
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<VirtualEntryState>(json) ?? new VirtualEntryState();
        }
        catch
        {
            return new VirtualEntryState();
        }
    }
}

public sealed class VirtualEntryState
{
    public string TargetSequence { get; set; } = "LL";
    public string ToleranceText { get; set; } = string.Empty;
}
