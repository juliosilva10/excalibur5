using Excalibur5.Models.Diversity;
using Excalibur5.Services.Strategy.Recovery;

namespace Excalibur5.Services.Strategy.Diversity;

/// <summary>
/// Connects a settled Diversity <see cref="GroupResult"/> to an <see cref="IRecoverStrategy"/>,
/// treating the whole group as a single contract: the group's net profit and total stake are fed
/// to the recover strategy once, and the next group's total stake (from recover) is redistributed
/// across the legs. Holds no broker state — pure orchestration over the recover interface.
/// </summary>
public sealed class DiversityRecoveryBridge
{
    private readonly IRecoverStrategy? _recover;
    private readonly decimal _baseTotalStake;
    private readonly decimal _baseTakeProfit;
    private readonly decimal _baseStopLoss;

    public DiversityRecoveryBridge(
        IRecoverStrategy? recover, decimal baseTotalStake,
        decimal baseTakeProfit = 0m, decimal baseStopLoss = 0m)
    {
        _recover = recover;
        _baseTotalStake = baseTotalStake;
        _baseTakeProfit = baseTakeProfit;
        _baseStopLoss = baseStopLoss;
    }

    /// <summary>
    /// Records a completed group's net result with the recover strategy (group = one contract).
    /// </summary>
    public void RecordGroupResult(GroupResult result)
        => _recover?.RecordResult(result.TotalProfit, result.TotalStake);

    /// <summary>
    /// Total stake to commit on the next group, per the recover strategy. Falls back to the base
    /// total when no recover is active.
    /// </summary>
    public decimal NextTotalStake(decimal currentTotalStake)
    {
        if (_recover == null) return _baseTotalStake;
        var ctx = new RecoverContext(_baseTotalStake, _baseTakeProfit, _baseStopLoss, currentTotalStake);
        return _recover.GetNextStake(ctx);
    }

    /// <summary>
    /// Produces the next group config: same legs, but with stakes redistributed to match the
    /// recover-decided total (proportional to each leg's configured weight).
    /// </summary>
    public DiversityGroupConfig BuildNextGroup(DiversityGroupConfig template, decimal currentTotalStake)
    {
        var target = NextTotalStake(currentTotalStake);
        var stakes = GroupStakeDistributor.Distribute(template.Legs, target);
        var legs = GroupStakeDistributor.ApplyStakes(template.Legs, stakes);
        return template with { Legs = legs };
    }
}
