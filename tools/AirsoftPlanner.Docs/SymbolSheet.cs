using AirsoftPlanner.App.Services;
using AirsoftPlanner.Core.Symbols;
using SkiaSharp;

namespace AirsoftPlanner.Docs;

/// <summary>Planche des symboles militaires disponibles, pour l'annexe de la documentation.</summary>
public static class SymbolSheet
{
    public static byte[] Render()
    {
        var symbols = MilitarySymbols.All.Where(s => s is not MilSymbol.Auto).ToList();
        const int columns = 3, cellWidth = 460, cellHeight = 110;
        var rows = (symbols.Count + columns - 1) / columns + 2;
        var info = new SKImageInfo(columns * cellWidth, rows * cellHeight);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        using var font = new SKFont(SKTypeface.FromFamilyName("Segoe UI"), 22);
        using var ink = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        string[] colors = ["#1565C0", "#C62828", "#2E7D32", "#6A1B9A"];

        for (var i = 0; i < symbols.Count; i++)
        {
            var (x, y) = (i % columns * cellWidth, i / columns * cellHeight);
            SkiaSymbolRenderer.Draw(canvas, new SKPoint(x + 60, y + 62), 44, MilitarySymbols.Draw(symbols[i]), colors[i % colors.Length]);
            canvas.DrawText(MilitarySymbols.Label(symbols[i]), x + 120, y + 70, font, ink);
        }

        // Indicateurs de taille sur un symbole d'infanterie.
        var echelons = Enum.GetValues<Echelon>().Where(e => e != Echelon.None).ToList();
        for (var i = 0; i < echelons.Count; i++)
        {
            var row = (symbols.Count + columns - 1) / columns + i / columns;
            var (x, y) = (i % columns * cellWidth, row * cellHeight);
            SkiaSymbolRenderer.Draw(canvas, new SKPoint(x + 60, y + 70), 44, MilitarySymbols.Draw(MilSymbol.Infantry, echelons[i]), "#1565C0");
            canvas.DrawText(MilitarySymbols.Label(echelons[i]), x + 120, y + 78, font, ink);
        }

        using var image = surface.Snapshot();
        return image.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }
}
