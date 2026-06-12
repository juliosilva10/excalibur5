using Excalibur5.Models.Strategy;
using Excalibur5.Services.Strategy.Virtual;

namespace Excalibur5.Tests;

internal static class SimulatorTests
{
    public static Task TickTradeUsesExactTickCount()
    {
        using var simulator = new VirtualTradeSimulator();
        VirtualTradeCompleted? completed = null;
        simulator.TradeCompleted += (_, result) => completed = result;

        TestAssert.True(simulator.TryStart(CreateRequest(SignalDirection.Call, 100m, ticks: 2)), "Trade did not start");
        simulator.UpdateSpot(101m);
        TestAssert.Null(completed, "Trade completed before the configured tick count");
        simulator.UpdateSpot(102m);

        TestAssert.NotNull(completed, "Trade did not complete");
        TestAssert.True(completed!.Won, "CALL with higher final price should win");
        return Task.CompletedTask;
    }

    public static Task OnlyOneSimulationIsPending()
    {
        using var simulator = new VirtualTradeSimulator();

        TestAssert.True(simulator.TryStart(CreateRequest(SignalDirection.Call, 100m, ticks: 2)), "First trade did not start");
        TestAssert.False(simulator.TryStart(CreateRequest(SignalDirection.Put, 100m, ticks: 2)), "Second trade was accepted");
        return Task.CompletedTask;
    }

    public static Task EqualFinalPriceIsLoss()
    {
        using var simulator = new VirtualTradeSimulator();
        VirtualTradeCompleted? completed = null;
        simulator.TradeCompleted += (_, result) => completed = result;

        simulator.TryStart(CreateRequest(SignalDirection.Put, 100m, ticks: 1));
        simulator.UpdateSpot(100m);

        TestAssert.NotNull(completed, "Trade did not complete");
        TestAssert.False(completed!.Won, "Equal price must be a loss");
        return Task.CompletedTask;
    }

    public static async Task TimedTradeRemainsPaused()
    {
        using var simulator = new VirtualTradeSimulator();
        var completion = new TaskCompletionSource<VirtualTradeCompleted>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        simulator.TradeCompleted += (_, result) => completion.TrySetResult(result);

        simulator.TryStart(CreateRequest(SignalDirection.Call, 100m, seconds: 1));
        simulator.UpdateSpot(101m);
        simulator.Pause();
        await Task.Delay(1200);

        TestAssert.False(completion.Task.IsCompleted, "Timed trade completed while paused");
        simulator.Resume();
        var result = await completion.Task.WaitAsync(TimeSpan.FromSeconds(2));
        TestAssert.True(result.Won, "CALL with higher final price should win");
    }

    private static VirtualTradeRequest CreateRequest(
        SignalDirection direction,
        decimal entrySpot,
        int seconds = 60,
        int ticks = 0)
    {
        return new VirtualTradeRequest(
            VirtualTradeIdGenerator.Next(),
            new TradeSignal { Direction = direction },
            entrySpot,
            seconds,
            ticks);
    }
}
