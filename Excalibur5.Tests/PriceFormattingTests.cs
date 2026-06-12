using Excalibur5.Models;

namespace Excalibur5.Tests;

internal static class PriceFormattingTests
{
    public static Task UsesSelectedPipSize()
    {
        TestAssert.Equal("4912.682", MarketPriceFormatter.Format(4912.682m, 3), "V10 format is incorrect");
        TestAssert.Equal("4912.6820", MarketPriceFormatter.Format(4912.682m, 4), "Four-pip format is incorrect");
        TestAssert.Equal("4912.68", MarketPriceFormatter.Format(4912.682m, 2), "Two-pip format is incorrect");
        return Task.CompletedTask;
    }
}
