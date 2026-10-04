using System;
using System.Collections.Generic;
using System.Linq;
using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Core.Geo;
using SkiaSharp;
using AirsoftPlanner.Core.Localization;

namespace AirsoftPlanner.App.Services;

/// <summary>Image de la carte du terrain avec les zones et le quadrillage UTM, pour les documents imprimés.</summary>
public static class MapSnapshot
{
    /// <param name="layer">Fond de carte.</param>
    /// <param name="zones">Zones à dessiner ; celles mises en avant (missions de l'équipe) sont plus marquées.</param>
    /// <param name="maxSide">Taille maximale de l'image produite, en pixels.</param>
    /// <param name="factionColor">Couleur d'une faction (symboles des points qui lui appartiennent).</param>
    public static byte[] Render(MapLayer layer, IEnumerable<(Zone Zone, bool Highlighted)> zones, int maxSide = 1800,
        Func<Guid, string?>? factionColor = null, IEnumerable<(string Color, IReadOnlyList<GeoPoint> Points)>? trails = null)
    {
        using var source = SKBitmap.Decode(layer.Image) ?? throw new InvalidOperationException(L.T("fond_de_carte_illisible"));
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

        // Trajets des équipes (RETEX), cercle au départ, rond plein à l'arrivée.
        foreach (var (trailColor, trailPoints) in trails ?? [])
        {
            if (trailPoints.Count < 2)
                continue;
            var color = SKColor.TryParse(trailColor, out var parsed) ? parsed : SKColors.Orange;
            using var path = new SKPath();
            path.AddPoly(trailPoints.Select(ToPixel).ToArray(), false);
            using var halo = new SKPaint { Color = SKColors.White.WithAlpha(170), IsStroke = true, StrokeWidth = 7, IsAntialias = true, StrokeJoin = SKStrokeJoin.Round };
            using var line = new SKPaint { Color = color, IsStroke = true, StrokeWidth = 4, IsAntialias = true, StrokeJoin = SKStrokeJoin.Round };
            canvas.DrawPath(path, halo);
            canvas.DrawPath(path, line);
            using var dot = new SKPaint { Color = color, IsAntialias = true };
            using var ring = new SKPaint { Color = SKColors.White, IsStroke = true, StrokeWidth = 3, IsAntialias = true };
            var (first, last) = (ToPixel(trailPoints[0]), ToPixel(trailPoints[^1]));
            canvas.DrawCircle(first, 7, ring);
            canvas.DrawCircle(last, 10, dot);
            canvas.DrawCircle(last, 10, ring);
        }

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
                var symbol = AirsoftPlanner.Core.Symbols.MilitarySymbols.Resolve(zone.Symbol, zone.Category);
                var symbolColor = zone.OwnerFactionId is { } owner && factionColor?.Invoke(owner) is { } c ? c : zone.Color;
                SkiaSymbolRenderer.Draw(canvas, points[0], Math.Max(22, info.Width / 55f) * (highlighted ? 1.2f : 1),
                    AirsoftPlanner.Core.Symbols.MilitarySymbols.Draw(symbol, zone.Echelon), symbolColor);
            }

            var center = new SKPoint(points.Average(p => p.X), points.Average(p => p.Y) - (zone.Kind == ZoneKind.Point ? Math.Max(30, info.Width / 40f) : 0));
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
    /// <summary>Quadrillage UTM : un quadrillage par fuseau visible, limite entre fuseaux en jaune.</summary>
    private static void DrawUtmGrid(SKCanvas canvas, SKImageInfo info, GeoBounds bounds, Func<GeoPoint, SKPoint> toPixel)
    {
        var midLatitude = (bounds.North + bounds.South) / 2;
        static int StandardZone(double longitude) => Math.Clamp((int)Math.Floor((longitude + 180) / 6) + 1, 1, 60);
        static double ZoneWest(int zone) => -180 + (zone - 1) * 6.0;
        var (firstZone, lastZone) = (StandardZone(bounds.West), StandardZone(bounds.East));
        var left = UtmCoordinate.FromGeo(new GeoPoint(midLatitude, bounds.West), firstZone);
        var right = UtmCoordinate.FromGeo(new GeoPoint(midLatitude, bounds.East), firstZone);
        var spacing = new[] { 100.0, 200, 500, 1000, 2000, 5000 }.FirstOrDefault(s => Math.Abs(right.Easting - left.Easting) / s <= 12, 5000);

        using var line = new SKPaint { Color = new SKColor(0, 0, 0, 150), IsStroke = true, StrokeWidth = 1.5f, IsAntialias = true };
        using var halo = new SKPaint { Color = new SKColor(255, 255, 255, 110), IsStroke = true, StrokeWidth = 4, IsAntialias = true };
        using var font = new SKFont(SKTypeface.Default, Math.Max(12, info.Width / 95f));
        using var text = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var textBackground = new SKPaint { Color = new SKColor(0, 0, 0, 170) };
        var french = AirsoftPlanner.Core.Localization.L.Culture;

        void Label(string value, float x, float y)
        {
            var width = font.MeasureText(value);
            canvas.DrawRect(x - 2, y - font.Size, width + 4, font.Size + 4, textBackground);
            canvas.DrawText(value, x, y, font, text);
        }

        var names = new List<string>();
        for (var zone = firstZone; zone <= lastZone; zone++)
        {
            var x0 = Math.Max(0, toPixel(new GeoPoint(midLatitude, ZoneWest(zone))).X);
            var x1 = Math.Min(info.Width, toPixel(new GeoPoint(midLatitude, ZoneWest(zone + 1))).X);
            if (x1 - x0 < 1)
                continue;

            var west = Math.Max(bounds.West, ZoneWest(zone));
            var east = Math.Min(bounds.East, ZoneWest(zone + 1));
            var band = UtmCoordinate.FromGeo(new GeoPoint(midLatitude, (west + east) / 2), zone).Band;
            names.Add($"{zone}{band}");
            var corners = new[]
            {
                new GeoPoint(bounds.North, west), new GeoPoint(bounds.North, east),
                new GeoPoint(bounds.South, west), new GeoPoint(bounds.South, east),
            }.Select(p => UtmCoordinate.FromGeo(p, zone)).ToList();
            var (minE, maxE) = (corners.Min(c => c.Easting), corners.Max(c => c.Easting));
            var (minN, maxN) = (corners.Min(c => c.Northing), corners.Max(c => c.Northing));
            UtmCoordinate Utm(double e, double n) => new(zone, band, e, n);

            canvas.Save();
            canvas.ClipRect(new SKRect(x0, 0, x1, info.Height));
            for (var e = Math.Ceiling(minE / spacing) * spacing; e <= maxE; e += spacing)
            {
                var (a, b) = (toPixel(Utm(e, minN).ToGeo()), toPixel(Utm(e, maxN).ToGeo()));
                canvas.DrawLine(a, b, halo);
                canvas.DrawLine(a, b, line);
                var topX = b.X + (a.X - b.X) * (-b.Y / Math.Max(1, a.Y - b.Y));
                if (topX >= x0 && topX <= x1)
                    Label((e / 1000).ToString(spacing >= 1000 ? "0" : "0.0", french), topX + 3, font.Size + 2);
            }

            for (var n = Math.Ceiling(minN / spacing) * spacing; n <= maxN; n += spacing)
            {
                var (a, b) = (toPixel(Utm(minE, n).ToGeo()), toPixel(Utm(maxE, n).ToGeo()));
                canvas.DrawLine(a, b, halo);
                canvas.DrawLine(a, b, line);
                Label((n / 1000).ToString(spacing >= 1000 ? "0" : "0.0", french), x0 + 3, a.Y + (b.Y - a.Y) * ((x0 - a.X) / Math.Max(1, b.X - a.X)) - 4);
            }

            canvas.Restore();
        }

        using var boundary = new SKPaint
        {
            Color = new SKColor(0xFF, 0xD6, 0x00), IsStroke = true, StrokeWidth = 3, IsAntialias = true,
            PathEffect = SKPathEffect.CreateDash([12, 6], 0),
        };
        using var boundaryHalo = new SKPaint { Color = new SKColor(0, 0, 0, 140), IsStroke = true, StrokeWidth = 6, IsAntialias = true };
        for (var zone = firstZone + 1; zone <= lastZone; zone++)
        {
            var x = toPixel(new GeoPoint(midLatitude, ZoneWest(zone))).X;
            canvas.DrawLine(x, 0, x, info.Height, boundaryHalo);
            canvas.DrawLine(x, 0, x, info.Height, boundary);
            Label($"◄ {zone - 1} | {zone} ►", x - font.MeasureText($"◄ {zone - 1} | {zone} ►") / 2, font.Size * 2.6f);
        }

        Label(L.F("quadrillage_utm_x_x", string.Join(" | ", names), (spacing >= 1000 ? $"{spacing / 1000:0} km" : $"{spacing:0} m"))
              + (names.Count > 1 ? L.T("limite_de_fuseau_en_jaune") : ""), 6, info.Height - 8);
    }
}
