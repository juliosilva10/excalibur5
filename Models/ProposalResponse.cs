namespace Excalibur5.Models;

public sealed record ProposalResponse
{
    public string ProposalId { get; init; } = string.Empty;
    public decimal AskPrice { get; init; }
    public decimal Payout { get; init; }
    public decimal Spot { get; init; }
    public long SpotTime { get; init; }
    public long DateExpiry { get; init; }
    public decimal PayoutPerPoint { get; init; }
    public string SubscriptionId { get; init; } = string.Empty;
    public string ContractType { get; init; } = string.Empty;
    public List<string> BarrierChoices { get; init; } = new();
}
