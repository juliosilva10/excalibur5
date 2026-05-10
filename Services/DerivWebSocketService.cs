using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using Excalibur5.Config;

namespace Excalibur5.Services;

public sealed class DerivWebSocketService : IDerivWebSocketService
{
    private const string Src = "WebSocket";
    private static readonly Uri ServerUri = new(AppConfig.WebSocketUrl);

    private ClientWebSocket?         _ws;
    private CancellationTokenSource  _appCts = new();
    private SemaphoreSlim             _sendLock = new(1, 1);
    private int  _reconnecting;
    private volatile bool _intentionalDisconnect;
    private volatile int  _reconnectDelay = AppConfig.ReconnectBaseDelayMs;

    public bool IsConnected => _ws?.State == WebSocketState.Open;

    public event EventHandler?         Connected;
    public event EventHandler?         Disconnected;
    public event EventHandler<string>? MessageReceived;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        _intentionalDisconnect = false;
        _reconnectDelay        = AppConfig.ReconnectBaseDelayMs;
        Interlocked.Exchange(ref _reconnecting, 0);

        AppLogger.Info(Src, $"ConnectAsync called → {AppConfig.WebSocketUrl}");
        await ConnectInternalAsync(ct);
    }

    private async Task ConnectInternalAsync(CancellationToken ct)
    {
        // Dispose only if not already disposed/aborted — avoids ObjectDisposedException
        if (_ws is not null)
        {
            try { _ws.Dispose(); } catch { /* ignore */ }
            _ws = null;
        }
        _ws = new ClientWebSocket();
        // Server-side keep-alive: send a WS ping frame every 30 s
        _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);

        // Reset the CTS so the new receive loop gets a fresh, uncancelled token
        if (_appCts.IsCancellationRequested)
        {
            _appCts.Dispose();
            _appCts = new CancellationTokenSource();
        }

        // Recriar o semáforo garante que não fica travado de uma conexão anterior
        var oldLock = _sendLock;
        _sendLock = new SemaphoreSlim(1, 1);
        oldLock.Dispose();

        AppLogger.Info(Src, "Connecting…");
        await _ws.ConnectAsync(ServerUri, ct);
        AppLogger.Info(Src, "Connected — state: Open");

        Connected?.Invoke(this, EventArgs.Empty);

        _ = Task.Run(() => ReceiveLoopAsync(_appCts.Token), _appCts.Token);
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        const int bufferSize = 32768;
        var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        using var messageStream = new System.IO.MemoryStream(4096);

        AppLogger.Info(Src, "Receive loop started");
        try
        {
            while (_ws is { State: WebSocketState.Open } && !ct.IsCancellationRequested)
            {
                messageStream.SetLength(0);
                WebSocketReceiveResult result;
                do
                {
                    result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer, 0, bufferSize), ct);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        AppLogger.Warn(Src, $"Server sent Close frame: {result.CloseStatus} — {result.CloseStatusDescription}");
                        await HandleDisconnectAsync(ct);
                        return;
                    }

                    messageStream.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                var json = Encoding.UTF8.GetString(messageStream.GetBuffer(), 0, (int)messageStream.Length);
                MessageReceived?.Invoke(this, json);
            }

            AppLogger.Info(Src, $"Receive loop exited — WS state: {_ws?.State}");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            AppLogger.Info(Src, "Receive loop cancelled (intentional shutdown)");
        }
        catch (Exception ex)
        {
            AppLogger.Error(Src, "Receive loop exception", ex);
            await HandleDisconnectAsync(ct);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task HandleDisconnectAsync(CancellationToken ct)
    {
        // Only one reconnect loop at a time
        if (Interlocked.CompareExchange(ref _reconnecting, 1, 0) != 0)
        {
            AppLogger.Info(Src, "HandleDisconnect: reconnect already in progress, skipping");
            return;
        }

        AppLogger.Warn(Src, "Disconnected — firing event");
        Disconnected?.Invoke(this, EventArgs.Empty);

        if (_intentionalDisconnect || ct.IsCancellationRequested)
        {
            Interlocked.Exchange(ref _reconnecting, 0);
            return;
        }

        AppLogger.Info(Src, "Starting reconnect loop…");
        while (!ct.IsCancellationRequested)
        {
            AppLogger.Info(Src, $"Reconnect attempt in {_reconnectDelay} ms…");
            try
            {
                await Task.Delay(_reconnectDelay, ct);
                await ConnectInternalAsync(ct);
                // Reset delay only after successful reconnect
                _reconnectDelay = AppConfig.ReconnectBaseDelayMs;
                AppLogger.Info(Src, "Reconnect successful");
                Interlocked.Exchange(ref _reconnecting, 0);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                AppLogger.Info(Src, "Reconnect loop cancelled");
                Interlocked.Exchange(ref _reconnecting, 0);
                return;
            }
            catch (Exception ex)
            {
                // Double delay after each failure, cap at max
                _reconnectDelay = Math.Min(_reconnectDelay * 2, AppConfig.ReconnectMaxDelayMs);
                AppLogger.Error(Src, $"Reconnect attempt failed (next in {_reconnectDelay} ms)", ex);
            }
        }

        Interlocked.Exchange(ref _reconnecting, 0);
    }

    public async Task DisconnectAsync()
    {
        _intentionalDisconnect = true;
        AppLogger.Info(Src, "DisconnectAsync called");

        // Cancel the receive loop first so it exits cleanly without triggering reconnect
        _appCts.Cancel();

        if (_ws is { State: WebSocketState.Open })
        {
            try
            {
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "User disconnect",
                    CancellationToken.None);
                AppLogger.Info(Src, "WebSocket closed gracefully");
            }
            catch (Exception ex)
            {
                AppLogger.Warn(Src, $"CloseAsync error (ignored): {ex.Message}");
            }
        }
        // Fire exactly once — the receive loop won't fire it again because _intentionalDisconnect=true
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    public async Task SendAsync(string json, CancellationToken ct = default)
    {
        // Capture local reference — _ws may be replaced by reconnect on another thread
        var ws = _ws;
        if (ws is not { State: WebSocketState.Open })
            throw new InvalidOperationException("WebSocket is not connected.");

        var byteCount = Encoding.UTF8.GetByteCount(json);
        var bytes = ArrayPool<byte>.Shared.Rent(byteCount);
        try
        {
            Encoding.UTF8.GetBytes(json, 0, json.Length, bytes, 0);
            var segment = new ArraySegment<byte>(bytes, 0, byteCount);

            await _sendLock.WaitAsync(ct);
            try
            {
                await ws.SendAsync(segment, WebSocketMessageType.Text, true, ct);
            }
            finally
            {
                _sendLock.Release();
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _intentionalDisconnect = true;
        AppLogger.Info(Src, "DisposeAsync called");

        // Cancel the receive loop and give it a moment to exit before disposing _ws
        _appCts.Cancel();

        // Small yield to let the receive loop observe cancellation before we dispose _ws
        await Task.Yield();

        var ws = _ws;
        if (ws is { State: WebSocketState.Open })
        {
            try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Dispose", CancellationToken.None); }
            catch { /* ignore */ }
        }
        ws?.Dispose();
        _appCts.Dispose();
        _sendLock.Dispose();
    }
}
