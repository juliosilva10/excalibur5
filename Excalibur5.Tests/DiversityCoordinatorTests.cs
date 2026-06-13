using Excalibur5.Models;
using Excalibur5.Models.Diversity;
using Excalibur5.Services;
using Excalibur5.Services.Strategy.Diversity;

namespace Excalibur5.Tests;

internal sealed class FakeContractService : IContractService
{
    public event EventHandler<ProposalResponse>? ProposalUpdated;
    public event EventHandler<OpenContractUpdate>? OpenContractUpdated;

    private long _nextId = 1000;
    // Contract types that should fail the buy (to exercise partial-failure unwind).
    public HashSet<string> FailBuyForTypes { get; } = new();
    public List<long> Subscribed { get; } = new();
    public List<long> Sold { get; } = new();
    public List<(string Symbol, string Type)> Bought { get; } = new();

    public Task<BuyResponse> BuyDirectAsync(string symbol, string contractType, decimal stake, int duration, string durationUnit, string? barrier = null, CancellationToken ct = default)
    {
        if (FailBuyForTypes.Contains(contractType))
            return Task.FromResult(new BuyResponse { Error = $"simulated failure for {contractType}" });

        var id = Interlocked.Increment(ref _nextId);
        Bought.Add((symbol, contractType));
        return Task.FromResult(new BuyResponse { ContractId = id, BuyPrice = stake, Payout = stake * 2 });
    }

    public Task SubscribeOpenContractAsync(long contractId, CancellationToken ct = default)
    {
        Subscribed.Add(contractId);
        return Task.CompletedTask;
    }

    public Task<SellResponse> SellContractAsync(long contractId, CancellationToken ct = default)
    {
        Sold.Add(contractId);
        return Task.FromResult(new SellResponse { ContractId = contractId });
    }

    /// <summary>Test helper: push a settlement update for a contract.</summary>
    public void PushSettlement(long contractId, decimal profit)
        => OpenContractUpdated?.Invoke(this, new OpenContractUpdate
        {
            ContractId = contractId,
            Profit = profit,
            IsSold = true,
            Status = profit >= 0 ? "won" : "lost"
        });

    // Unused members for these tests.
    public Task<ContractsForResponse> GetContractsForAsync(string symbol, string currency = "USD", CancellationToken ct = default) => Task.FromResult(new ContractsForResponse());
    public Task<ProposalResponse> SubscribeProposalAsync(string symbol, string contractType, decimal amount, int? duration = null, string? durationUnit = null, long? dateExpiry = null, string? barrier = null, string currency = "USD", string? subscriptionKey = null, CancellationToken ct = default) => Task.FromResult(new ProposalResponse());
    public Task UnsubscribeProposalAsync(string? contractType = null, CancellationToken ct = default) => Task.CompletedTask;
    public Task UnsubscribeAllProposalsAsync(CancellationToken ct = default) => Task.CompletedTask;
    public void ClearSubscription(string contractType) { }
    public Task<BuyResponse> BuyContractAsync(string proposalId, decimal price, CancellationToken ct = default) => Task.FromResult(new BuyResponse { ContractId = 1 });
    public Task UnsubscribeOpenContractAsync(long contractId, CancellationToken ct = default) => Task.CompletedTask;
    public Task<List<ProfitTableEntry>> GetProfitTableAsync(int limit = 50, int offset = 0, CancellationToken ct = default) => Task.FromResult(new List<ProfitTableEntry>());
    public Task<(string EntrySpot, string ExitSpot)> GetContractSpotsAsync(long contractId, CancellationToken ct = default) => Task.FromResult(("", ""));
    public Task<OpenContractUpdate?> GetContractStatusAsync(long contractId, CancellationToken ct = default) => Task.FromResult<OpenContractUpdate?>(null);
}

internal static class DiversityCoordinatorTests
{
    private static DiversityGroupConfig TwoLegConfig(string callType = "DIGITOVER", string putType = "DIGITUNDER")
        => new()
        {
            Kind = DiversityGroupKind.Hedge,
            Legs = new[]
            {
                new DiversityLeg { Symbol = "R_10", ContractType = callType, Barrier = "5", Stake = 10m, DurationTicks = 1 },
                new DiversityLeg { Symbol = "R_10", ContractType = putType, Barrier = "5", Stake = 10m, DurationTicks = 1 }
            }
        };

    public static async Task BuysAllLegsAndConsolidates()
    {
        var svc = new FakeContractService();
        using var coord = new DiversityCoordinator(svc);

        GroupResult? completed = null;
        coord.GroupCompleted += (_, e) => completed = e.Result;

        var groupId = await coord.ExecuteGroupAsync(TwoLegConfig());
        TestAssert.NotNull(groupId, "Group should be created");
        TestAssert.Equal(2, svc.Bought.Count, "Both legs bought");
        TestAssert.Equal(2, svc.Subscribed.Count, "Both legs subscribed");
        TestAssert.Equal(1, coord.PendingGroupCount, "One group pending settlement");

        // Over wins +8, Under loses -10 → net -2
        svc.PushSettlement(svc.Subscribed[0], 8m);
        TestAssert.Null(completed, "Not complete after one leg");
        svc.PushSettlement(svc.Subscribed[1], -10m);

        TestAssert.NotNull(completed, "Group should complete after both legs");
        TestAssert.Equal(-2m, completed!.TotalProfit, "Net -2");
        TestAssert.False(completed.Won, "Negative net → not won");
        TestAssert.Equal(0, coord.PendingGroupCount, "No pending group after completion");
    }

    public static async Task PartialFailureUnwindsBoughtLegs()
    {
        var svc = new FakeContractService();
        svc.FailBuyForTypes.Add("DIGITUNDER"); // second leg fails
        using var coord = new DiversityCoordinator(svc);

        var groupId = await coord.ExecuteGroupAsync(TwoLegConfig());
        TestAssert.Null(groupId, "Group should abort on leg failure");
        TestAssert.Equal(1, svc.Bought.Count, "Only first leg bought before failure");
        TestAssert.Equal(1, svc.Sold.Count, "The bought leg must be sold back (unwind)");
        TestAssert.Equal(0, coord.PendingGroupCount, "No group registered on abort");
    }

    public static async Task FirstLegFailureBuysNothing()
    {
        var svc = new FakeContractService();
        svc.FailBuyForTypes.Add("DIGITOVER"); // first leg fails immediately
        using var coord = new DiversityCoordinator(svc);

        var groupId = await coord.ExecuteGroupAsync(TwoLegConfig());
        TestAssert.Null(groupId, "Group aborts");
        TestAssert.Equal(0, svc.Bought.Count, "No legs bought");
        TestAssert.Equal(0, svc.Sold.Count, "Nothing to unwind");
    }
}
