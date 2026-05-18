using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace Excalibur5.Views.Controls;

public partial class OpenPositionsView : UserControl
{
    public OpenPositionsView()
    {
        InitializeComponent();
    }
}

public sealed class ProgressWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double progress) return 0.0;
        double maxWidth = 44;
        if (parameter is string s && double.TryParse(s, CultureInfo.InvariantCulture, out var mw))
            maxWidth = mw;
        return progress * maxWidth;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class ProfitToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush GreenBrush = CreateFrozen(0x34, 0xc7, 0x59);
    private static readonly SolidColorBrush RedBrush = CreateFrozen(0xFF, 0x6B, 0x6B);
    private static readonly SolidColorBrush NeutralBrush = CreateFrozen(0xa3, 0xb8, 0xcc);

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (parameter is string p && p == "type")
        {
            var label = value as string ?? "";
            return label is "Call" or "Rise" ? GreenBrush : RedBrush;
        }

        if (value is decimal profit)
        {
            if (profit > 0) return GreenBrush;
            if (profit < 0) return RedBrush;
        }
        return NeutralBrush;
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
