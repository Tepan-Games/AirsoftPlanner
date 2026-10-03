using System.Collections.Generic;

namespace AirsoftPlanner.App;

/// <summary>Couleurs proposées pour les factions et les zones (bien distinctes sur une carte).</summary>
public static class ColorPalette
{
    public static IReadOnlyList<string> Colors { get; } =
    [
        "#C62828", // rouge
        "#1565C0", // bleu
        "#2E7D32", // vert
        "#F9A825", // jaune
        "#EF6C00", // orange
        "#6A1B9A", // violet
        "#00838F", // cyan
        "#AD1457", // rose
        "#4E342E", // marron
        "#546E7A", // gris
        "#212121", // noir
        "#FAFAFA", // blanc
    ];
}
