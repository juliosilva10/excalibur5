using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Excalibur5.Models;

namespace Excalibur5.Converters;

public sealed class TickDirectionToColorConverter : IValueConverter
{
    private static readonly SolidColorBrush Green = new(Color.FromRgb(0x34, 0xc7, 0x59));
    private static readonly SolidColorBrush Red   = new(Color.FromRgb(0xFF, 0x6B, 0x6B));
    private static readonly SolidColorBrush Flat  = new(Color.FromRgb(0xa3, 0xb8, 0xcc));

    static TickDirectionToColorConverter()
    {
        Green.Freeze();
        Red.Freeze();
        Flat.Freeze();
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is TickDirection dir)
        {
            return dir switch
            {
                TickDirection.Up   => Green,
                TickDirection.Down => Red,
                _                 => Flat
            };
        }
        return Flat;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
