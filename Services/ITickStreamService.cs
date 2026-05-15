using Excalibur5.Models;

namespace Excalibur5.Services;

public interface ITickStreamService
{
    event EventHandler<TickData>? TickReceived;

    bool IsConnected { get; }

    Task<string> SubscribeAsync(string symbol, CancellationToken ct = default);
    Task UnsubscribeAsync(string symbol, CancellationToken ct = default);
    Task UnsubscribeAllAsync(CancellationToken ct = default);
    Task ForgetAllTicksAsync(CancellationToken ct = default);
    Task<List<TickData>> GetHistoryAsync(string symbol, int count = 1000, CancellationToken ct = default);
    Task<List<CandleData>> GetCandleHistoryAsync(string symbol, int granularity = 60, int count = 1000, CancellationToken ct = default);

    /// <summary>Clears local subscription tracking without sending forget to server.</summary>
    void ClearSubscription(string symbol);
}
