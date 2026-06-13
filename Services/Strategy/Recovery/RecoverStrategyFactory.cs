using Excalibur5.Models;
using Excalibur5.Models.Strategy;

namespace Excalibur5.Services.Strategy.Recovery;

public static class RecoverStrategyFactory
{
    public static IRecoverStrategy? Create(StrategyConfig config)
    {
        return config.RecoverMode switch
        {
            RecoverModeKeys.Martingale => new MartingaleRecoverStrategy(config.MartingaleFactor, config.MartingaleMaxLevel),
            RecoverModeKeys.Deficit => new DeficitRecoverStrategy(config.DeficitMaxStake, config.DeficitRecoveryTrades),
            _ => null
        };
    }
}
