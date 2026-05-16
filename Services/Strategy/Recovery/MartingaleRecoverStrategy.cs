namespace Excalibur5.Services.Strategy.Recovery;

public sealed class MartingaleRecoverStrategy : IRecoverStrategy
{
    private const int PayoutHistorySize = 5;
    private const decimal DefaultPayoutRatio = 0.5m;

    private readonly decimal _factor;
    private readonly int _maxLevel;
    private readonly decimal[] _payoutRatios = new decimal[PayoutHistorySize];
    private int _payoutIndex;
    private int _payoutCount;
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
        if (_level <= 0)
            return context.BaseTakeProfit;

        var avgRatio = GetAveragePayoutRatio();
        return Math.Round(context.CurrentStake * avgRatio, 2);
    }

    public decimal GetDynamicStopLoss(RecoverContext context)
    {
        if (_level <= 0)
            return context.BaseStopLoss;

        if (context.BaseStake <= 0)
            return context.BaseStopLoss;

        var ratio = context.CurrentStake / context.BaseStake;
        return Math.Round(context.BaseStopLoss * ratio, 2);
    }

    public void RecordResult(decimal profit, decimal stakeUsed)
    {
        if (profit >= 0)
        {
            if (stakeUsed > 0)
                RecordPayoutRatio(profit / stakeUsed);
            _level = 0;
        }
        else if (_level < _maxLevel)
            _level++;
        else
            _level = 0;
    }

    public void Reset()
    {
        _level = 0;
        _payoutCount = 0;
        _payoutIndex = 0;
    }

    private void RecordPayoutRatio(decimal ratio)
    {
        _payoutRatios[_payoutIndex] = ratio;
        _payoutIndex = (_payoutIndex + 1) % PayoutHistorySize;
        if (_payoutCount < PayoutHistorySize)
            _payoutCount++;
    }

    private decimal GetAveragePayoutRatio()
    {
        if (_payoutCount == 0)
            return DefaultPayoutRatio;

        decimal sum = 0;
        for (int i = 0; i < _payoutCount; i++)
            sum += _payoutRatios[i];
        return sum / _payoutCount;
    }
}
