using System.Globalization;
using System.Windows.Data;

namespace Excalibur5.Converters;

[ValueConversion(typeof(object), typeof(bool))]
public sealed class StringMatchToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null || parameter == null) return false;
        return string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is true && parameter != null)
            return parameter.ToString()!;
        return Binding.DoNothing;
    }
}
