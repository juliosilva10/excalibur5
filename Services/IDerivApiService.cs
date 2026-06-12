using Excalibur5.Models;

namespace Excalibur5.Services;

public interface IDerivApiService
{
    event EventHandler<AuthorizeResponse>? Authorized;
    event EventHandler<BalanceResponse>?   BalanceUpdated;

    /// <summary>
    /// Authenticates via the new Deriv Options platform flow:
    /// 1. REST: GET /accounts → discover account ID
    /// 2. REST: POST /otp → get authenticated WebSocket URL
    /// 3. WebSocket: connect (already authenticated — no authorize message needed)
    /// Fires <see cref="Authorized"/> on success.
    /// </summary>
    Task ConnectAndAuthorizeAsync(string patToken, CancellationToken ct = default);

    Task<long>           PingAsync(CancellationToken ct = default);
    Task<DateTimeOffset> GetServerTimeAsync(CancellationToken ct = default);
    Task SubscribeBalanceAsync(CancellationToken ct = default);
}
