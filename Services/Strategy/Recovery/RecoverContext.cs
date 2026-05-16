namespace Excalibur5.Services.Strategy.Recovery;

public sealed record RecoverContext(
    decimal BaseStake,
    decimal BaseTakeProfit,
    decimal BaseStopLoss,
    decimal CurrentStake);
