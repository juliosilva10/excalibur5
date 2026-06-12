namespace Excalibur5.Tests;

internal static class TestAssert
{
    public static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void False(bool condition, string message) => True(!condition, message);

    public static void Equal<T>(T expected, T actual, string message)
        where T : IEquatable<T>
    {
        if (!expected.Equals(actual))
            throw new InvalidOperationException($"{message}. Expected {expected}, got {actual}.");
    }

    public static void Null(object? value, string message) => True(value == null, message);

    public static void NotNull(object? value, string message) => True(value != null, message);
}
