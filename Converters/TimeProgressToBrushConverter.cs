using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Excalibur5.Converters;

public sealed class TimeProgressToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush BlueBrush = CreateFrozen(0x00, 0xb4, 0xd8);
    private static readonly SolidColorBrush YellowBrush = CreateFrozen(0xf0, 0xc0, 0x40);
    private static readonly SolidColorBrush RedBrush = CreateFrozen(0xff, 0x44, 0x44);

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double progress)
            return BlueBrush;

        if (progress >= 0.8)
            return RedBrush;
        if (progress >= 0.5)
            return YellowBrush;
        return BlueBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static SolidColorBrush CreateFrozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
