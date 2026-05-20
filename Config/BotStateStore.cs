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
    public bool UseDuration { get; set; } = true;
    public string DurationUnit { get; set; } = "Minutes";
    public string DurationText { get; set; } = "5";
    public string DirectionMode { get; set; } = "Ambos";
    public string StakeText { get; set; } = "10";
    public string TakeProfitText { get; set; } = "5.00";
    public string StopLossText { get; set; } = "3.00";
    public string MaxContractsText { get; set; } = "3";
    public double ConfidenceThreshold { get; set; } = 0.70;
    public bool EnableEma { get; set; } = true;
    public bool EnableRsi { get; set; } = true;
    public bool EnableSupportResistance { get; set; } = true;
    public bool EnableMacd { get; set; } = true;
    public bool EnableBollinger { get; set; } = true;
    public bool EnableCandlePattern { get; set; } = true;
    public bool EnableMomentum { get; set; } = true;
    public bool EnableTrailingStop { get; set; } = true;
    public string RecoverMode { get; set; } = string.Empty;
    public string StrategyMode { get; set; } = "Multi-Indicador";
    public string SampleSizeText { get; set; } = "5";
    public string DeficitMaxStakeText { get; set; } = "50";
    public string DeficitRecoveryTradesText { get; set; } = "1";

    // Tick Scalper
    public int TickScalperCooldown { get; set; } = 5;
    public double TickScalperThreshold { get; set; } = 0.70;
    public int TickScalperMinAgreement { get; set; } = 2;
    public bool TickScalperFlatFilter { get; set; } = true;

    // Candle Dynamics
    public int CandleDynamicsCooldown { get; set; } = 10;
    public double CandleDynamicsThreshold { get; set; } = 0.55;
    public int CandleDynamicsMinStreak { get; set; } = 3;
}
