using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Excalibur5.Converters;

/// <summary>
/// Converts a confidence value (0.3–1.0) to a SolidColorBrush
/// transitioning: red → orange → yellow → blue → green.
/// </summary>
[ValueConversion(typeof(double), typeof(SolidColorBrush))]
public sealed class ConfidenceToColorConverter : IValueConverter
{
    private static readonly (double Position, Color Color)[] Stops =
    [
        (0.30, Color.FromRgb(0xe7, 0x4c, 0x3c)), // Red
        (0.45, Color.FromRgb(0xf3, 0x9c, 0x12)), // Orange
        (0.55, Color.FromRgb(0xf1, 0xc4, 0x0f)), // Yellow
        (0.65, Color.FromRgb(0x00, 0xb4, 0xd8)), // Blue
        (0.80, Color.FromRgb(0x34, 0xc7, 0x59)), // Green
    ];

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var v = value is double d ? d : 0.5;
        v = Math.Clamp(v, 0.30, 1.0);

        // Find the two stops to interpolate between
        for (int i = 0; i < Stops.Length - 1; i++)
        {
            if (v <= Stops[i + 1].Position)
            {
                var t = (v - Stops[i].Position) / (Stops[i + 1].Position - Stops[i].Position);
                var color = Lerp(Stops[i].Color, Stops[i + 1].Color, t);
                return new SolidColorBrush(color);
            }
        }

        // Above last stop — use green
        return new SolidColorBrush(Stops[^1].Color);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;

    private static Color Lerp(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }
}
