using Excalibur5.Models;

namespace Excalibur5.Services;

public interface IContractService
{
    event EventHandler<ProposalResponse>? ProposalUpdated;

    Task<ContractsForResponse> GetContractsForAsync(string symbol, string currency = "USD", CancellationToken ct = default);
    Task<ProposalResponse> SubscribeProposalAsync(string symbol, string contractType, decimal amount, int? duration = null, string? durationUnit = null, long? dateExpiry = null, string? barrier = null, string currency = "USD", CancellationToken ct = default);
    Task UnsubscribeProposalAsync(string? contractType = null, CancellationToken ct = default);
    Task UnsubscribeAllProposalsAsync(CancellationToken ct = default);
    void ClearSubscription(string contractType);
    Task<BuyResponse> BuyContractAsync(string proposalId, decimal price, CancellationToken ct = default);
}
