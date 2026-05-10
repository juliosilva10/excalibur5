using Excalibur5.Models;

namespace Excalibur5.Services;

public interface IDerivApiService
{
    event EventHandler<AuthorizeResponse>? Authorized;
    event EventHandler<BalanceResponse>?   BalanceUpdated;

    Task AuthorizeAsync(string token, CancellationToken ct = default);
    Task<long>           PingAsync(CancellationToken ct = default);
    Task<DateTimeOffset> GetServerTimeAsync(CancellationToken ct = default);
    Task SubscribeBalanceAsync(CancellationToken ct = default);
}
