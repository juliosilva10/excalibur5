using Excalibur5.Models.Strategy;

namespace Excalibur5.Services.Strategy.Recovery;

public static class RecoverStrategyFactory
{
    public static IRecoverStrategy? Create(StrategyConfig config)
    {
        return config.RecoverMode switch
        {
            "Martingale" => new MartingaleRecoverStrategy(config.MartingaleFactor, config.MartingaleMaxLevel),
            "Deficit Recovery" => new DeficitRecoverStrategy(config.DeficitMaxStake, config.DeficitRecoveryTrades),
            _ => null
        };
    }
}
