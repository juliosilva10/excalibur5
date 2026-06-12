namespace Excalibur5.Services;

public interface IDerivWebSocketService : IAsyncDisposable
{
    bool IsConnected { get; }

    event EventHandler?        Connected;
    event EventHandler?        Disconnected;
    event EventHandler<string>? MessageReceived;

    /// <summary>
    /// Connects to the WebSocket using the default URL (legacy).
    /// </summary>
    Task ConnectAsync(CancellationToken ct = default);

    /// <summary>
    /// Connects to a specific WebSocket URL (e.g. the OTP-authenticated URL
    /// from the new Deriv Options platform).
    /// </summary>
    Task ConnectAsync(Uri url, CancellationToken ct = default);

    Task DisconnectAsync();
    Task SendAsync(string json, CancellationToken ct = default);
}
