using System;
using System.Globalization;
using System.Linq;
using AirsoftPlanner.Core.Symbols;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AirsoftPlanner.App.Controls;

/// <summary>Dessine un symbole militaire (cadre aux couleurs de la faction, pictogramme noir).</summary>
public static class SymbolRenderer
{
    private static readonly Typeface Bold = new(FontFamily.Default, FontStyle.Normal, FontWeight.Bold);

    /// <param name="height">Hauteur du cadre en pixels.</param>
    /// <param name="halo">Contour supplémentaire (état d'une équipe : juste, en retard...), ou null.</param>
    public static void Draw(DrawingContext context, Point center, double height, SymbolDrawing symbol, string factionColor, IBrush? halo = null)
    {
        var (r, g, b) = MilitarySymbols.FillColor(factionColor);
        var fill = new SolidColorBrush(Color.FromRgb(r, g, b));
        var stroke = new Pen(Brushes.Black, Math.Max(1.2, height / 16), lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        var width = height * symbol.Aspect;
        var box = new Rect(center.X - width / 2, center.Y - height / 2, width, height);
        Point P(float x, float y) => new(box.X + x * box.Width, box.Y + y * box.Height);

        switch (symbol.Frame)
        {
            case SymbolFrame.Dot:
                context.DrawEllipse(new SolidColorBrush(ParseColor(factionColor)), new Pen(Brushes.White, 2), center, height / 3, height / 3);
                return;
            case SymbolFrame.Rectangle:
                if (halo is not null)
                    context.DrawRectangle(null, new Pen(halo, 4), box.Inflate(3), 3, 3);
                context.DrawRectangle(fill, stroke, box);
                break;
            case SymbolFrame.Circle:
                if (halo is not null)
                    context.DrawEllipse(null, new Pen(halo, 4), center, height / 2 + 3, height / 2 + 3);
                context.DrawEllipse(fill, stroke, center, height / 2, height / 2);
                break;
            case SymbolFrame.Triangle:
                context.DrawGeometry(fill, stroke, Polygon([P(0.5f, 0), P(1, 1), P(0, 1)], true));
                break;
        }

        foreach (var shape in symbol.Shapes)
        {
            switch (shape)
            {
                case SymbolLine line:
                    context.DrawLine(stroke, P(line.X1, line.Y1), P(line.X2, line.Y2));
                    break;
                case SymbolEllipse ellipse:
                    context.DrawEllipse(ellipse.Filled ? Brushes.Black : null, stroke, P(ellipse.X, ellipse.Y),
                        ellipse.RadiusX * box.Width, ellipse.RadiusY * box.Height);
                    break;
                case SymbolPolygon polygon:
                    context.DrawGeometry(polygon.Filled ? Brushes.Black : null, stroke,
                        Polygon(polygon.Points.Select(p => P(p.X, p.Y)).ToArray(), polygon.Closed));
                    break;
                case SymbolText text:
                    var formatted = new FormattedText(text.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Bold,
                        Math.Max(6, text.Size * box.Height), Brushes.Black);
                    var at = P(text.X, text.Y);
                    context.DrawText(formatted, new Point(at.X - formatted.Width / 2, at.Y - formatted.Height / 2));
                    break;
            }
        }
    }

    private static StreamGeometry Polygon(Point[] points, bool closed)
    {
        var geometry = new StreamGeometry();
        using var c = geometry.Open();
        c.BeginFigure(points[0], closed);
        foreach (var point in points.Skip(1))
            c.LineTo(point);
        c.EndFigure(closed);
        return geometry;
    }

    private static Color ParseColor(string hex) => Color.TryParse(hex, out var color) ? color : Colors.Orange;
}

/// <summary>Aperçu d'un symbole militaire (listes de choix, zones).</summary>
public class SymbolView : Control
{
    public static readonly StyledProperty<MilSymbol> SymbolProperty =
        AvaloniaProperty.Register<SymbolView, MilSymbol>(nameof(Symbol), MilSymbol.Dot);

    public static readonly StyledProperty<Echelon> EchelonProperty =
        AvaloniaProperty.Register<SymbolView, Echelon>(nameof(Echelon));

    public static readonly StyledProperty<string> ColorProperty =
        AvaloniaProperty.Register<SymbolView, string>(nameof(Color), "#1565C0");

    static SymbolView() => AffectsRender<SymbolView>(SymbolProperty, EchelonProperty, ColorProperty);

    public MilSymbol Symbol { get => GetValue(SymbolProperty); set => SetValue(SymbolProperty, value); }

    public Echelon Echelon { get => GetValue(EchelonProperty); set => SetValue(EchelonProperty, value); }

    public string Color { get => GetValue(ColorProperty); set => SetValue(ColorProperty, value); }

    public override void Render(DrawingContext context)
    {
        var drawing = MilitarySymbols.Draw(Symbol, Echelon);
        // Place pour l'indicateur de taille au-dessus et la hampe du QG en dessous.
        var height = Math.Min(Bounds.Height / 1.9, Bounds.Width / (drawing.Aspect + 0.2));
        SymbolRenderer.Draw(context, new Point(Bounds.Width / 2, Bounds.Height * 0.47), height, drawing, Color);
    }
}
