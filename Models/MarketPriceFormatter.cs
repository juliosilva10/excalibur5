using System.Globalization;

namespace Excalibur5.Models;

public static class MarketPriceFormatter
{
    public static string Format(decimal price, int pipSize)
    {
        return price.ToString($"F{Math.Max(0, pipSize)}", CultureInfo.InvariantCulture);
    }
}
