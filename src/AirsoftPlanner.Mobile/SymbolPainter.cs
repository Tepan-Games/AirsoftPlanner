using AirsoftPlanner.Core.Symbols;
using Android.Graphics;

namespace AirsoftPlanner.Mobile;

/// <summary>Symboles militaires sur la carte du téléphone (couleur de la faction ; rouge en mode nuit).</summary>
internal static class SymbolPainter
{
    public static void Draw(Canvas canvas, float x, float y, float height, SymbolDrawing symbol, string factionColor, bool night)
    {
        var (r, g, b) = MilitarySymbols.FillColor(factionColor);
        var inkColor = night ? Color.Rgb(220, 40, 40) : Color.Black;
        using var fill = new Paint(PaintFlags.AntiAlias) { Color = night ? Color.Rgb(40, 0, 0) : Color.Rgb(r, g, b) };
        using var stroke = new Paint(PaintFlags.AntiAlias) { Color = inkColor, StrokeWidth = Math.Max(2.5f, height / 14) };
        stroke.SetStyle(Paint.Style.Stroke);
        stroke.StrokeCap = Paint.Cap.Round;
        stroke.StrokeJoin = Paint.Join.Round;
        using var ink = new Paint(PaintFlags.AntiAlias) { Color = inkColor, FakeBoldText = true };
        var width = height * symbol.Aspect;
        var (left, top) = (x - width / 2, y - height / 2);
        PointF P(float px, float py) => new(left + px * width, top + py * height);

        switch (symbol.Frame)
        {
            case SymbolFrame.Dot:
                var (dr, dg, db) = MilitarySymbols.ParseHex(factionColor);
                using (var dot = new Paint(PaintFlags.AntiAlias) { Color = night ? Color.Rgb(170, 20, 20) : Color.Rgb(dr, dg, db) })
                    canvas.DrawCircle(x, y, height / 3, dot);
                return;
            case SymbolFrame.Rectangle:
                canvas.DrawRect(left, top, left + width, top + height, fill);
                canvas.DrawRect(left, top, left + width, top + height, stroke);
                break;
            case SymbolFrame.Circle:
                canvas.DrawCircle(x, y, height / 2, fill);
                canvas.DrawCircle(x, y, height / 2, stroke);
                break;
            case SymbolFrame.Triangle:
                using (var triangle = Polygon([P(0.5f, 0), P(1, 1), P(0, 1)], true))
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
                    var (a, c) = (P(line.X1, line.Y1), P(line.X2, line.Y2));
                    canvas.DrawLine(a.X, a.Y, c.X, c.Y, stroke);
                    break;
                case SymbolEllipse ellipse:
                    var at = P(ellipse.X, ellipse.Y);
                    var (rx, ry) = (ellipse.RadiusX * width, ellipse.RadiusY * height);
                    canvas.DrawOval(at.X - rx, at.Y - ry, at.X + rx, at.Y + ry, ellipse.Filled ? ink : stroke);
                    break;
                case SymbolPolygon polygon:
                    using (var path = Polygon(polygon.Points.Select(p => P(p.X, p.Y)).ToArray(), polygon.Closed))
                        canvas.DrawPath(path, polygon.Filled ? ink : stroke);
                    break;
                case SymbolText text:
                    ink.TextSize = Math.Max(14, text.Size * height);
                    var position = P(text.X, text.Y);
                    canvas.DrawText(text.Text, position.X - ink.MeasureText(text.Text) / 2, position.Y + ink.TextSize * 0.36f, ink);
                    break;
            }
        }
    }

    private static Android.Graphics.Path Polygon(PointF[] points, bool closed)
    {
        var path = new Android.Graphics.Path();
        path.MoveTo(points[0].X, points[0].Y);
        foreach (var point in points.Skip(1))
            path.LineTo(point.X, point.Y);
        if (closed)
            path.Close();
        return path;
    }
}
