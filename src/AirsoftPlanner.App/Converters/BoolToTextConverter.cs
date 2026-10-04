using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace AirsoftPlanner.App.Converters;

/// <summary>Affiche un texte quand la valeur est vraie, rien sinon.</summary>
public class BoolToTextConverter(string whenTrue) : IValueConverter
{
    /// <summary>Repère de l'équipe chef de faction dans le plan radio.</summary>
    public static BoolToTextConverter CommandStar { get; } = new("★");

    /// <summary>Fréquence en double dans le plan radio.</summary>
    public static BoolToTextConverter DuplicateWarning { get; } = new("⚠");

    public static BoolToTextConverter DuplicateFrequencyTip { get; } = new("Fréquence en double : voir l'alerte en haut de la fenêtre");

    // Rien quand c'est faux (null : pas d'infobulle vide).
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true ? whenTrue : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
