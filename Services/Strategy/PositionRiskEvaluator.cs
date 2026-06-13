namespace Excalibur5.Services.Strategy;

/// <summary>
/// Decision produced by <see cref="PositionRiskEvaluator"/> for an open position update.
/// </summary>
/// <param name="NewStopLoss">A trailing stop-loss value to apply, or null to leave it unchanged.</param>
/// <param name="ShouldSell">True when the position should be sold now.</param>
/// <param name="Reason">Human-readable explanation, for logging.</param>
public readonly record struct RiskDecision(decimal? NewStopLoss, bool ShouldSell, string Reason);

/// <summary>
/// Pure take-profit / stop-loss / trailing-stop evaluation for an open position.
/// Extracted from StrategyExecutor so the money-management math can be unit-tested in
/// isolation. Contains no state, no I/O, no logging — callers apply the returned decision.
/// </summary>
public static class PositionRiskEvaluator
{
    public static RiskDecision Evaluate(
        decimal profit,
        decimal currentDynamicStopLoss,
        decimal effectiveTakeProfit,
        decimal effectiveStopLoss,
        bool enableTrailingStop,
        long entryEpoch,
        long expiryEpoch,
        long nowEpoch,
        bool isValidToSell)
    {
        decimal? newSl = null;
        var dynamicSl = currentDynamicStopLoss;

        // Trailing stop: ratchet the stop up as profit approaches the take-profit target.
        if (enableTrailingStop && profit > 0)
        {
            if (profit >= effectiveTakeProfit * 0.9m)
            {
                var candidate = effectiveTakeProfit * 0.5m;
                if (candidate > dynamicSl)
                {
                    newSl = candidate;
                    dynamicSl = candidate;
                }
            }
            else if (profit >= effectiveTakeProfit * 0.7m)
            {
                if (dynamicSl < 0)
                {
                    newSl = 0m;
                    dynamicSl = 0m;
                }
            }
        }

        // Take profit.
        if (profit >= effectiveTakeProfit)
        {
            return isValidToSell
                ? new RiskDecision(newSl, true, $"TP hit: profit={profit:F2} >= {effectiveTakeProfit:F2}")
                : new RiskDecision(newSl, false, "tp-wait-invalid-to-sell");
        }

        // Time-decayed stop loss: tighten the allowed loss as the contract nears expiry.
        var totalDuration = expiryEpoch - entryEpoch;
        var timeRemaining = Math.Max(expiryEpoch - nowEpoch, 1);
        var timeRatio = totalDuration > 0 ? (decimal)timeRemaining / totalDuration : 0m;
        var timeSl = -(effectiveStopLoss * timeRatio);

        // Use the tighter of the trailing SL and the time-based SL.
        var effectiveSl = enableTrailingStop
            ? Math.Max(dynamicSl, timeSl)
            : timeSl;

        if (profit <= effectiveSl)
        {
            return isValidToSell
                ? new RiskDecision(newSl, true, $"SL hit: profit={profit:F2} <= {effectiveSl:F2} (time ratio={timeRatio:F2})")
                : new RiskDecision(newSl, false, "sl-wait-invalid-to-sell");
        }

        return new RiskDecision(newSl, false, "hold");
    }
}
