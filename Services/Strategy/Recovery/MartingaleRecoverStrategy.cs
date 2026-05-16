namespace Excalibur5.Services.Strategy.Recovery;

public sealed class MartingaleRecoverStrategy : IRecoverStrategy
{
    private readonly decimal _factor;
    private readonly int _maxLevel;
    private int _level;

    public MartingaleRecoverStrategy(decimal factor, int maxLevel)
    {
        _factor = factor;
        _maxLevel = maxLevel;
    }

    public decimal GetNextStake(RecoverContext context)
    {
        if (_level <= 0)
            return context.BaseStake;

        var stake = context.BaseStake;
        for (int i = 0; i < _level; i++)
            stake *= _factor;
        return Math.Round(stake, 2);
    }

    public decimal GetDynamicTakeProfit(RecoverContext context)
    {
        return context.BaseTakeProfit;
    }

    public decimal GetDynamicStopLoss(RecoverContext context)
    {
        return context.BaseStopLoss;
    }

    public void RecordResult(decimal profit, decimal stakeUsed)
    {
        if (profit >= 0)
            _level = 0;
        else if (_level < _maxLevel)
            _level++;
        else
            _level = 0;
    }

    public void Reset() => _level = 0;
}
