namespace Excalibur5.Services;

public interface IDerivWebSocketService : IAsyncDisposable
{
    bool IsConnected { get; }

    event EventHandler?        Connected;
    event EventHandler?        Disconnected;
    event EventHandler<string>? MessageReceived;

    Task ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync();
    Task SendAsync(string json, CancellationToken ct = default);
}
