using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;

namespace Excalibur5.Converters;

[ValueConversion(typeof(string), typeof(TextBlock))]
public sealed class SequenceToColoredTextConverter : IValueConverter
{
    private static readonly SolidColorBrush GreenBrush = new((Color)ColorConverter.ConvertFromString("#34c759"));
    private static readonly SolidColorBrush RedBrush   = new((Color)ColorConverter.ConvertFromString("#FF6B6B"));
    private static readonly FontFamily Consolas = new("Consolas");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = value as string ?? string.Empty;
        var tb = new TextBlock();

        for (int i = 0; i < text.Length; i++)
        {
            if (i > 0)
                tb.Inlines.Add(new Run("  ") { FontSize = 8 });

            var ch = text[i];
            tb.Inlines.Add(new Run(ch.ToString())
            {
                Foreground = ch is 'W' or 'w' ? GreenBrush : RedBrush,
                FontFamily = Consolas,
                FontSize = 12,
                FontWeight = FontWeights.Bold
            });
        }

        return tb;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => DependencyProperty.UnsetValue;
}
