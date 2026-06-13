namespace Excalibur5.Models;

/// <summary>
/// Centralized string keys for strategy/recover/direction modes. These values double as
/// UI dropdown labels AND comparison keys across ViewModels and Services, so defining them
/// once prevents a typo from silently disabling a comparison (which the compiler can't catch
/// on a raw string literal).
/// </summary>
public static class StrategyModeKeys
{
    public const string MultiIndicator = "Multi-Indicador";
    public const string Trend = "Tendência";
    public const string TickScalper = "Tick Scalper";
    public const string CandleDynamics = "Candle Dynamics";

    public static readonly IReadOnlyList<string> All =
        [MultiIndicator, Trend, TickScalper, CandleDynamics];
}

public static class RecoverModeKeys
{
    public const string None = "";
    public const string Martingale = "Martingale";
    public const string Deficit = "Deficit Recovery";

    /// <summary>Modes selectable including the empty (off) option.</summary>
    public static readonly IReadOnlyList<string> WithNone = [None, Martingale, Deficit];

    /// <summary>Only the active recover strategies (no empty option).</summary>
    public static readonly IReadOnlyList<string> Active = [Martingale, Deficit];
}

public static class DirectionModeKeys
{
    public const string Both = "Ambos";
    public const string Call = "Call";
    public const string Put = "Put";
}
