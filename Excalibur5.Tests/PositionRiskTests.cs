using Excalibur5.Services.Strategy;

namespace Excalibur5.Tests;

internal static class PositionRiskTests
{
    // A position comfortably in profit past the TP target should be sold.
    public static Task TakeProfitTriggersSell()
    {
        var d = PositionRiskEvaluator.Evaluate(
            profit: 5.00m, currentDynamicStopLoss: -3m,
            effectiveTakeProfit: 5.00m, effectiveStopLoss: 3.00m,
            enableTrailingStop: false,
            entryEpoch: 1000, expiryEpoch: 1060, nowEpoch: 1010,
            isValidToSell: true);

        TestAssert.True(d.ShouldSell, "TP should trigger a sell");
        return Task.CompletedTask;
    }

    // TP reached but the contract isn't valid to sell yet → hold, don't sell.
    public static Task TakeProfitWaitsWhenNotValidToSell()
    {
        var d = PositionRiskEvaluator.Evaluate(
            profit: 5.00m, currentDynamicStopLoss: -3m,
            effectiveTakeProfit: 5.00m, effectiveStopLoss: 3.00m,
            enableTrailingStop: false,
            entryEpoch: 1000, expiryEpoch: 1060, nowEpoch: 1010,
            isValidToSell: false);

        TestAssert.False(d.ShouldSell, "Should not sell while invalid to sell");
        return Task.CompletedTask;
    }

    // At >=90% of TP, the trailing stop ratchets up to 50% of TP.
    public static Task TrailingStopRatchetsAt90Percent()
    {
        var d = PositionRiskEvaluator.Evaluate(
            profit: 4.60m, currentDynamicStopLoss: -3m,
            effectiveTakeProfit: 5.00m, effectiveStopLoss: 3.00m,
            enableTrailingStop: true,
            entryEpoch: 1000, expiryEpoch: 1060, nowEpoch: 1010,
            isValidToSell: true);

        TestAssert.NotNull(d.NewStopLoss, "Trailing SL should be set at >=90% of TP");
        TestAssert.Equal(2.50m, d.NewStopLoss!.Value, "Trailing SL should be 50% of TP");
        TestAssert.False(d.ShouldSell, "Should not sell yet (below TP)");
        return Task.CompletedTask;
    }

    // At >=70% (but <90%) of TP, the trailing stop moves to breakeven.
    public static Task TrailingStopMovesToBreakevenAt70Percent()
    {
        var d = PositionRiskEvaluator.Evaluate(
            profit: 3.60m, currentDynamicStopLoss: -3m,
            effectiveTakeProfit: 5.00m, effectiveStopLoss: 3.00m,
            enableTrailingStop: true,
            entryEpoch: 1000, expiryEpoch: 1060, nowEpoch: 1010,
            isValidToSell: true);

        TestAssert.NotNull(d.NewStopLoss, "Trailing SL should be set at >=70% of TP");
        TestAssert.Equal(0m, d.NewStopLoss!.Value, "Trailing SL should be breakeven");
        return Task.CompletedTask;
    }

    // A losing position past the (time-decayed) stop loss should be sold.
    // With full time remaining, timeSl == -stopLoss, so profit <= -3 triggers.
    public static Task StopLossTriggersSell()
    {
        var d = PositionRiskEvaluator.Evaluate(
            profit: -3.00m, currentDynamicStopLoss: -3m,
            effectiveTakeProfit: 5.00m, effectiveStopLoss: 3.00m,
            enableTrailingStop: false,
            entryEpoch: 1000, expiryEpoch: 1060, nowEpoch: 1000,
            isValidToSell: true);

        TestAssert.True(d.ShouldSell, "SL should trigger a sell");
        return Task.CompletedTask;
    }

    // A modest profit between SL and TP should hold (no sell, no SL change with trailing off).
    public static Task HoldsBetweenThresholds()
    {
        var d = PositionRiskEvaluator.Evaluate(
            profit: 1.00m, currentDynamicStopLoss: -3m,
            effectiveTakeProfit: 5.00m, effectiveStopLoss: 3.00m,
            enableTrailingStop: false,
            entryEpoch: 1000, expiryEpoch: 1060, nowEpoch: 1010,
            isValidToSell: true);

        TestAssert.False(d.ShouldSell, "Should hold between SL and TP");
        TestAssert.Null(d.NewStopLoss, "No SL change with trailing disabled");
        return Task.CompletedTask;
    }
}
