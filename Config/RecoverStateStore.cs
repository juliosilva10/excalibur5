using System.IO;
using System.Text.Json;

namespace Excalibur5.Config;

public static class RecoverStateStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Excalibur5", "recoverstate.json");

    public static void Save(RecoverState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(state));
    }

    public static RecoverState Load()
    {
        if (!File.Exists(FilePath)) return new RecoverState();
        try
        {
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<RecoverState>(json) ?? new RecoverState();
        }
        catch
        {
            return new RecoverState();
        }
    }
}

public class RecoverState
{
    public string StakeText { get; set; } = "0.35";
    public string FactorText { get; set; } = "2.00";
    public string MaxLevelText { get; set; } = "3";
    public bool IsEnabled { get; set; }
}
