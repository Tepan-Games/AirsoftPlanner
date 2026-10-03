using System;
using System.Collections.Generic;
using System.Linq;
using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Core.Geo;
using SkiaSharp;

namespace AirsoftPlanner.App.Services;

/// <summary>Image de la carte du terrain avec les zones et le quadrillage UTM, pour les documents imprimés.</summary>
public static class MapSnapshot
{
    /// <param name="layer">Fond de carte.</param>
    /// <param name="zones">Zones à dessiner ; celles mises en avant (missions de l'équipe) sont plus marquées.</param>
    /// <param name="maxSide">Taille maximale de l'image produite, en pixels.</param>
    public static byte[] Render(MapLayer layer, IEnumerable<(Zone Zone, bool Highlighted)> zones, int maxSide = 1800)
    {
        using var source = SKBitmap.Decode(layer.Image) ?? throw new InvalidOperationException("Fond de carte illisible.");
        var scale = Math.Min(1.0, (double)maxSide / Math.Max(source.Width, source.Height));
        var info = new SKImageInfo((int)(source.Width * scale), (int)(source.Height * scale));
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        using (var image = SKImage.FromBitmap(source))
            canvas.DrawImage(image, new SKRect(0, 0, info.Width, info.Height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));

        var bounds = layer.Bounds;
        SKPoint ToPixel(GeoPoint point)
        {
            var (x, y) = bounds.ToRelative(point);
            return new SKPoint((float)(x * info.Width), (float)(y * info.Height));
        }

        DrawUtmGrid(canvas, info, bounds, ToPixel);

        using var font = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), Math.Max(14, info.Width / 70f));
        foreach (var (zone, highlighted) in zones.OrderBy(z => z.Highlighted).Where(z => z.Zone.Points.Count > 0))
        {
            var color = SKColor.TryParse(zone.Color, out var parsed) ? parsed : SKColors.Orange;
            var alpha = highlighted ? (byte)255 : (byte)140;
            var points = zone.Points.Select(ToPixel).ToArray();
            if (zone.Kind == ZoneKind.Area && points.Length >= 3)
            {
                using var path = new SKPath();
                path.AddPoly(points);
                using var fill = new SKPaint { Color = color.WithAlpha(highlighted ? (byte)90 : (byte)45), IsAntialias = true };
                using var stroke = new SKPaint { Color = color.WithAlpha(alpha), IsStroke = true, StrokeWidth = highlighted ? 5 : 2.5f, IsAntialias = true };
                canvas.DrawPath(path, fill);
                canvas.DrawPath(path, stroke);
            }
            else
            {
                using var dot = new SKPaint { Color = color.WithAlpha(alpha), IsAntialias = true };
                using var ring = new SKPaint { Color = SKColors.White, IsStroke = true, StrokeWidth = 3, IsAntialias = true };
                canvas.DrawCircle(points[0], highlighted ? 11 : 8, dot);
                canvas.DrawCircle(points[0], highlighted ? 11 : 8, ring);
            }

            var center = new SKPoint(points.Average(p => p.X), points.Average(p => p.Y) - (zone.Kind == ZoneKind.Point ? 22 : 0));
            DrawLabel(canvas, font, zone.Name, center, highlighted);
        }

        using var snapshot = surface.Snapshot();
        using var data = snapshot.Encode(SKEncodedImageFormat.Jpeg, 88);
        return data.ToArray();
    }

    private static void DrawLabel(SKCanvas canvas, SKFont font, string text, SKPoint center, bool highlighted)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        var width = font.MeasureText(text);
        var box = new SKRect(center.X - width / 2 - 6, center.Y - font.Size * 0.8f, center.X + width / 2 + 6, center.Y + font.Size * 0.45f);
        using var background = new SKPaint { Color = new SKColor(16, 16, 16, highlighted ? (byte)210 : (byte)150), IsAntialias = true };
        using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        canvas.DrawRoundRect(box, 4, 4, background);
        canvas.DrawText(text, center.X - width / 2, center.Y + font.Size * 0.2f, font, paint);
    }

    /// <summary>Quadrillage UTM (pas de 100 m à 1 km selon la taille), pour lire les coordonnées sur papier.</summary>
    private static void DrawUtmGrid(SKCanvas canvas, SKImageInfo info, GeoBounds bounds, Func<GeoPoint, SKPoint> toPixel)
    {
        var center = new GeoPoint((bounds.North + bounds.South) / 2, (bounds.East + bounds.West) / 2);
        var zone = UtmCoordinate.ZoneOf(center);
        var band = UtmCoordinate.FromGeo(center).Band;
        var corners = new[]
        {
            new GeoPoint(bounds.North, bounds.West), new GeoPoint(bounds.North, bounds.East),
            new GeoPoint(bounds.South, bounds.West), new GeoPoint(bounds.South, bounds.East),
        }.Select(p => UtmCoordinate.FromGeo(p, zone)).ToList();
        var (minE, maxE) = (corners.Min(c => c.Easting), corners.Max(c => c.Easting));
        var (minN, maxN) = (corners.Min(c => c.Northing), corners.Max(c => c.Northing));
        var spacing = new[] { 100.0, 200, 500, 1000, 2000, 5000 }.FirstOrDefault(s => (maxE - minE) / s <= 12, 5000);

        using var line = new SKPaint { Color = new SKColor(0, 0, 0, 150), IsStroke = true, StrokeWidth = 1.5f, IsAntialias = true };
        using var halo = new SKPaint { Color = new SKColor(255, 255, 255, 110), IsStroke = true, StrokeWidth = 4, IsAntialias = true };
        using var font = new SKFont(SKTypeface.Default, Math.Max(12, info.Width / 95f));
        using var text = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var textBackground = new SKPaint { Color = new SKColor(0, 0, 0, 170) };
        UtmCoordinate Utm(double e, double n) => new(zone, band, e, n);

        void Label(string value, float x, float y)
        {
            var width = font.MeasureText(value);
            canvas.DrawRect(x - 2, y - font.Size, width + 4, font.Size + 4, textBackground);
            canvas.DrawText(value, x, y, font, text);
        }

        for (var e = Math.Ceiling(minE / spacing) * spacing; e <= maxE; e += spacing)
        {
            var (a, b) = (toPixel(Utm(e, minN).ToGeo()), toPixel(Utm(e, maxN).ToGeo()));
            canvas.DrawLine(a, b, halo);
            canvas.DrawLine(a, b, line);
            Label((e / 1000).ToString(spacing >= 1000 ? "0" : "0.0", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")),
                b.X + (a.X - b.X) * (-b.Y / Math.Max(1, a.Y - b.Y)) + 3, font.Size + 2);
        }

        for (var n = Math.Ceiling(minN / spacing) * spacing; n <= maxN; n += spacing)
        {
            var (a, b) = (toPixel(Utm(minE, n).ToGeo()), toPixel(Utm(maxE, n).ToGeo()));
            canvas.DrawLine(a, b, halo);
            canvas.DrawLine(a, b, line);
            Label((n / 1000).ToString(spacing >= 1000 ? "0" : "0.0", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")),
                3, a.Y + (b.Y - a.Y) * (-a.X / Math.Max(1, b.X - a.X)) - 4);
        }

        Label($"Quadrillage UTM {zone}{band} · {(spacing >= 1000 ? $"{spacing / 1000:0} km" : $"{spacing:0} m")}", 6, info.Height - 8);
    }
}
