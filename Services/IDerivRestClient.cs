namespace Excalibur5.Services;

/// <summary>
/// REST client for Deriv Options platform endpoints
/// (account discovery, OTP generation for WebSocket).
/// </summary>
public interface IDerivRestClient
{
    /// <summary>
    /// Returns the account ID (e.g. "DOT90004580") and account type
    /// ("demo" or "real") for the first Options trading account
    /// associated with the given PAT.
    /// </summary>
    Task<(string AccountId, string AccountType)> GetAccountIdAsync(string patToken, CancellationToken ct = default);

    /// <summary>
    /// Calls the OTP endpoint and returns a ready-to-use authenticated
    /// WebSocket URL (demo or real depending on the account).
    /// </summary>
    Task<string> GetWebSocketUrlAsync(string accountId, string patToken, CancellationToken ct = default);
}
