using System.Globalization;
using System.Windows.Data;
using Excalibur5.Models;

namespace Excalibur5.Converters;

/// <summary>Displays a <see cref="DurationUnitType"/> with a friendly pt-BR label in combos.</summary>
[ValueConversion(typeof(DurationUnitType), typeof(string))]
public sealed class DurationUnitToLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is DurationUnitType u ? ToLabel(u) : string.Empty;

    public static string ToLabel(DurationUnitType u) => u switch
    {
        DurationUnitType.Ticks => "Ticks",
        DurationUnitType.Seconds => "Segundos",
        DurationUnitType.Minutes => "Minutos",
        DurationUnitType.Hours => "Horas",
        DurationUnitType.Days => "Dias",
        _ => u.ToString()
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
