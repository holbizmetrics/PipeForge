using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace PipeForge.GUI.Converters;

/// <summary>
/// Converts a bool to one of two colors. Usage:
/// Foreground="{Binding IsError, Converter={StaticResource BoolToColor}, ConverterParameter=#F38BA8|#BAC2DE}"
/// True = first color, False = second color.
/// </summary>
public class BoolToColorConverter : IValueConverter
{
    public static readonly BoolToColorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b && parameter is string colors)
        {
            var parts = colors.Split('|');
            if (parts.Length == 2)
            {
                var color = b ? parts[0] : parts[1];
                return new SolidColorBrush(Color.Parse(color));
            }
        }
        return new SolidColorBrush(Color.Parse("#CDD6F4"));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
