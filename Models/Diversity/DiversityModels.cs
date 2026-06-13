namespace Excalibur5.Models.Diversity;

/// <summary>How a Diversity group's legs are triggered.</summary>
public enum DiversityTriggerMode
{
    /// <summary>User fires the whole group with a button/command.</summary>
    Manual,
    /// <summary>A single strategy signal fires all legs together.</summary>
    Signal
}

/// <summary>
/// Relationship between the legs of a group — affects how risk should be read in the UI.
/// Hedge legs are anti-correlated on one tick; Diversification legs are independent across markets.
/// </summary>
public enum DiversityGroupKind
{
    /// <summary>Legs share one market/tick (e.g. Over + Under) — a true hedge.</summary>
    Hedge,
    /// <summary>Legs span different markets — independent outcomes, not a hedge.</summary>
    Diversification
}

/// <summary>
/// One leg of a Diversity group: a contract on a specific market, with its own type, barrier and
/// stake. Barrier is the digit prediction (0-9) for digit contracts, or a price barrier otherwise.
/// </summary>
public sealed record DiversityLeg
{
    public required string Symbol { get; init; }
    public required string ContractType { get; init; }
    /// <summary>Digit (0-9) for digit contracts; price barrier string otherwise; null if none.</summary>
    public string? Barrier { get; init; }
    public required decimal Stake { get; init; }
    public int DurationTicks { get; init; } = 1;

    /// <summary>Optional display label, e.g. "Over 5 @ V10".</summary>
    public string? Label { get; init; }
}

/// <summary>
/// Configuration of a Diversity group: the legs plus how they're triggered and how they relate.
/// </summary>
public sealed record DiversityGroupConfig
{
    public required IReadOnlyList<DiversityLeg> Legs { get; init; }
    public DiversityTriggerMode TriggerMode { get; init; } = DiversityTriggerMode.Manual;
    public DiversityGroupKind Kind { get; init; } = DiversityGroupKind.Hedge;

    /// <summary>Total stake committed across all legs.</summary>
    public decimal TotalStake
    {
        get
        {
            decimal sum = 0m;
            foreach (var leg in Legs) sum += leg.Stake;
            return sum;
        }
    }
}

/// <summary>
/// Consolidated result of a Diversity group once every leg has settled. This is what feeds recover
/// and virtual: the group is treated as a single contract whose profit is the net of all legs.
/// </summary>
public sealed record GroupResult(
    Guid GroupId,
    decimal TotalProfit,
    decimal TotalStake,
    bool Won,
    IReadOnlyList<long> ContractIds)
{
    /// <summary>Net payout ratio of the group (profit / stake) — drives Deficit Recovery sizing.</summary>
    public decimal NetPayoutRatio => TotalStake > 0 ? TotalProfit / TotalStake : 0m;
}
