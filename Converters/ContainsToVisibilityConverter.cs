using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Excalibur5.Models;

namespace Excalibur5.Converters;

[ValueConversion(typeof(IEnumerable), typeof(Visibility))]
public sealed class ContainsToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not IEnumerable<DurationUnitType> units || parameter is not string paramStr)
            return Visibility.Collapsed;

        if (!Enum.TryParse<DurationUnitType>(paramStr, out var unit))
            return Visibility.Collapsed;

        foreach (var u in units)
        {
            if (u == unit)
                return Visibility.Visible;
        }

        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
