namespace Excalibur5.Services.Strategy.Virtual;

public sealed record VirtualEntrySettings(string TargetSequence, int Tolerance)
{
    public bool Enabled => TargetSequence.Length > 0;

    public static VirtualEntrySettings Disabled { get; } = new(string.Empty, 0);
}

public interface IVirtualEntrySettingsProvider
{
    VirtualEntrySettings GetSettings();
}

public static class VirtualTradeIdGenerator
{
    private static long _nextId;

    public static long Next() => Interlocked.Decrement(ref _nextId);
}
