namespace Excalibur5.Config;

public static class AppConfig
{
    public const string WebSocketUrl = "wss://ws.derivws.com/websockets/v3?app_id=82663";
    public const int ReconnectBaseDelayMs = 1000;
    public const int ReconnectMaxDelayMs  = 60_000;
    public const int RequestTimeoutMs     = 15_000;
    public const int PingIntervalMs       = 1_000;
}
