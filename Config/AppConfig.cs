namespace Excalibur5.Config;

public static class AppConfig
{
    public const string WebSocketUrl = "wss://api.derivws.com/trading/v1/options/ws/public";
    public const int ReconnectBaseDelayMs = 1000;
    public const int ReconnectMaxDelayMs  = 60_000;
    public const int RequestTimeoutMs     = 15_000;
    public const int PingIntervalMs       = 1_000;
}
