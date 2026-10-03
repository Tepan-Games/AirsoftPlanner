using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace AirsoftPlanner.App.Converters;

/// <summary>Affiche un texte quand la valeur est vraie, rien sinon.</summary>
public class BoolToTextConverter(string whenTrue) : IValueConverter
{
    /// <summary>Repère de l'équipe chef de faction dans le plan radio.</summary>
    public static BoolToTextConverter CommandStar { get; } = new("★");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true ? whenTrue : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
