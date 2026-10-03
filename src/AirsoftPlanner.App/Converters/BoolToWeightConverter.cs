using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace AirsoftPlanner.App.Converters;

/// <summary>Vrai → gras, faux → normal.</summary>
public class BoolToWeightConverter : IValueConverter
{
    public static BoolToWeightConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? FontWeight.Bold : FontWeight.Normal;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
