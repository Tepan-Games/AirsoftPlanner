using System;
using System.Linq;
using AirsoftPlanner.Core.Symbols;
using SkiaSharp;

namespace AirsoftPlanner.App.Services;

/// <summary>Symboles militaires dans les images générées (cartes des ordres de mission).</summary>
public static class SkiaSymbolRenderer
{
    public static void Draw(SKCanvas canvas, SKPoint center, float height, SymbolDrawing symbol, string factionColor)
    {
        var (r, g, b) = MilitarySymbols.FillColor(factionColor);
        using var fill = new SKPaint { Color = new SKColor(r, g, b), IsAntialias = true };
        using var stroke = new SKPaint
        {
            Color = SKColors.Black, IsStroke = true, StrokeWidth = Math.Max(1.5f, height / 16), IsAntialias = true,
            StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round,
        };
        using var ink = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        var width = height * symbol.Aspect;
        var box = new SKRect(center.X - width / 2, center.Y - height / 2, center.X + width / 2, center.Y + height / 2);
        SKPoint P(float x, float y) => new(box.Left + x * box.Width, box.Top + y * box.Height);

        switch (symbol.Frame)
        {
            case SymbolFrame.Dot:
                var (dr, dg, db) = MilitarySymbols.ParseHex(factionColor);
                using (var dot = new SKPaint { Color = new SKColor(dr, dg, db), IsAntialias = true })
                    canvas.DrawCircle(center, height / 3, dot);
                using (var ring = new SKPaint { Color = SKColors.White, IsStroke = true, StrokeWidth = 3, IsAntialias = true })
                    canvas.DrawCircle(center, height / 3, ring);
                return;
            case SymbolFrame.Rectangle:
                canvas.DrawRect(box, fill);
                canvas.DrawRect(box, stroke);
                break;
            case SymbolFrame.Circle:
                canvas.DrawCircle(center, height / 2, fill);
                canvas.DrawCircle(center, height / 2, stroke);
                break;
            case SymbolFrame.Triangle:
                using (var triangle = Path([P(0.5f, 0), P(1, 1), P(0, 1)], true))
                {
                    canvas.DrawPath(triangle, fill);
                    canvas.DrawPath(triangle, stroke);
                }

                break;
        }

        foreach (var shape in symbol.Shapes)
        {
            switch (shape)
            {
                case SymbolLine line:
                    canvas.DrawLine(P(line.X1, line.Y1), P(line.X2, line.Y2), stroke);
                    break;
                case SymbolEllipse ellipse:
                    var oval = new SKRect(0, 0, ellipse.RadiusX * 2 * box.Width, ellipse.RadiusY * 2 * box.Height);
                    var at = P(ellipse.X, ellipse.Y);
                    oval.Offset(at.X - oval.Width / 2, at.Y - oval.Height / 2);
                    canvas.DrawOval(oval, ellipse.Filled ? ink : stroke);
                    break;
                case SymbolPolygon polygon:
                    using (var path = Path(polygon.Points.Select(p => P(p.X, p.Y)).ToArray(), polygon.Closed))
                        canvas.DrawPath(path, polygon.Filled ? ink : stroke);
                    break;
                case SymbolText text:
                    using (var font = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), Math.Max(7, text.Size * box.Height)))
                    {
                        var position = P(text.X, text.Y);
                        var textWidth = font.MeasureText(text.Text);
                        canvas.DrawText(text.Text, position.X - textWidth / 2, position.Y + font.Size * 0.36f, font, ink);
                    }

                    break;
            }
        }
    }

    private static SKPath Path(SKPoint[] points, bool closed)
    {
        var path = new SKPath();
        path.AddPoly(points, closed);
        return path;
    }
}
