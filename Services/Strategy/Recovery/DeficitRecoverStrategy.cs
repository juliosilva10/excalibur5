namespace Excalibur5.Services.Strategy.Recovery;

public sealed class DeficitRecoverStrategy : IRecoverStrategy
{
    private const int PayoutHistorySize = 5;
    private const decimal DefaultPayoutRatio = 0.5m;

    private readonly decimal _maxStake;
    private readonly int _recoveryTrades;
    private readonly decimal[] _payoutRatios = new decimal[PayoutHistorySize];
    private int _payoutIndex;
    private int _payoutCount;
    private decimal _deficit;

    public DeficitRecoverStrategy(decimal maxStake, int recoveryTrades)
    {
        _maxStake = maxStake;
        _recoveryTrades = Math.Max(1, recoveryTrades);
    }

    public decimal GetNextStake(RecoverContext context)
    {
        if (_deficit <= 0)
            return context.BaseStake;

        var avgRatio = GetAveragePayoutRatio();
        var needed = _deficit / (avgRatio * _recoveryTrades);
        var stake = Math.Max(needed, context.BaseStake);
        return Math.Round(Math.Min(stake, _maxStake), 2);
    }

    public decimal GetDynamicTakeProfit(RecoverContext context)
    {
        if (_deficit <= 0)
            return context.BaseTakeProfit;

        var avgRatio = GetAveragePayoutRatio();
        return Math.Round(context.CurrentStake * avgRatio, 2);
    }

    public decimal GetDynamicStopLoss(RecoverContext context)
    {
        if (_deficit <= 0)
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
            _deficit = Math.Max(0, _deficit - profit);
            if (stakeUsed > 0)
                RecordPayoutRatio(profit / stakeUsed);
        }
        else
        {
            _deficit += Math.Abs(profit);
        }
    }

    public void Reset()
    {
        _deficit = 0;
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
