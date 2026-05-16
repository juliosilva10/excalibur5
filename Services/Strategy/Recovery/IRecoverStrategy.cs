namespace Excalibur5.Services.Strategy.Recovery;

public interface IRecoverStrategy
{
    decimal GetNextStake(RecoverContext context);
    void RecordResult(decimal profit, decimal stakeUsed);
    void Reset();
}
