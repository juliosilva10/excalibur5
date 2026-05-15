using Excalibur5.Models;

namespace Excalibur5.Services;

public interface IContractService
{
    event EventHandler<ProposalResponse>? ProposalUpdated;
    event EventHandler<OpenContractUpdate>? OpenContractUpdated;

    Task<ContractsForResponse> GetContractsForAsync(string symbol, string currency = "USD", CancellationToken ct = default);
    Task<ProposalResponse> SubscribeProposalAsync(string symbol, string contractType, decimal amount, int? duration = null, string? durationUnit = null, long? dateExpiry = null, string? barrier = null, string currency = "USD", string? subscriptionKey = null, CancellationToken ct = default);
    Task UnsubscribeProposalAsync(string? contractType = null, CancellationToken ct = default);
    Task UnsubscribeAllProposalsAsync(CancellationToken ct = default);
    void ClearSubscription(string contractType);
    Task<BuyResponse> BuyContractAsync(string proposalId, decimal price, CancellationToken ct = default);
    Task<BuyResponse> BuyDirectAsync(string symbol, string contractType, decimal stake, int duration, string durationUnit, string? barrier = null, CancellationToken ct = default);
    Task SubscribeOpenContractAsync(long contractId, CancellationToken ct = default);
    Task UnsubscribeOpenContractAsync(long contractId, CancellationToken ct = default);
    Task<SellResponse> SellContractAsync(long contractId, CancellationToken ct = default);
    Task<List<ProfitTableEntry>> GetProfitTableAsync(int limit = 50, int offset = 0, CancellationToken ct = default);
    Task<(string EntrySpot, string ExitSpot)> GetContractSpotsAsync(long contractId, CancellationToken ct = default);
}
