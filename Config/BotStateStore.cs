using System.IO;
using System.Text.Json;

namespace Excalibur5.Config;

public static class BotStateStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Excalibur5", "botstate.json");

    public static void Save(BotState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(state));
    }

    public static BotState Load()
    {
        if (!File.Exists(FilePath)) return new BotState();
        try
        {
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<BotState>(json) ?? new BotState();
        }
        catch
        {
            return new BotState();
        }
    }
}

public class BotState
{
    public int Timeframe { get; set; } = 60;
    public string DirectionMode { get; set; } = "Ambos";
    public string StakeText { get; set; } = "10";
    public string TakeProfitText { get; set; } = "5.00";
    public string StopLossText { get; set; } = "3.00";
    public string MaxContractsText { get; set; } = "3";
    public int DurationMinutes { get; set; } = 5;
    public double ConfidenceThreshold { get; set; } = 0.70;
    public bool EnableEma { get; set; } = true;
    public bool EnableRsi { get; set; } = true;
    public bool EnableSupportResistance { get; set; } = true;
}
