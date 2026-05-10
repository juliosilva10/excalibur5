namespace Excalibur5.Models;

public sealed class PingResult
{
    public long           RoundTripMs { get; init; }
    public DateTimeOffset ServerUtc   { get; init; }
}
