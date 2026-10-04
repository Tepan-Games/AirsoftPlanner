using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace AirsoftPlanner.App.Converters;

/// <summary>Convertit une couleur « #RRGGBB » en pinceau.</summary>
public class HexColorToBrushConverter : IValueConverter
{
    public static HexColorToBrushConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string hex && Color.TryParse(hex, out var color) ? new SolidColorBrush(color) : Brushes.Transparent;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
