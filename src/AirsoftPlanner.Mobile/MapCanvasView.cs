using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Core.Gps;
using Android.Content;
using Android.Graphics;
using Android.Views;
using AirsoftPlanner.Core.Localization;

namespace AirsoftPlanner.Mobile;

/// <summary>Carte du terrain (image envoyée par le PC de l'OP) avec sa position, celles des alliés et la zone de la mission.</summary>
public class MapCanvasView(Context context) : View(context)
{
    private readonly Paint _ownPaint = new(PaintFlags.AntiAlias) { Color = Color.Rgb(21, 101, 192) };
    private readonly Paint _allyPaint = new(PaintFlags.AntiAlias) { Color = Color.Rgb(46, 125, 50) };
    private readonly Paint _targetPaint = new(PaintFlags.AntiAlias) { Color = Color.Rgb(198, 40, 40), StrokeWidth = 6 };
    private readonly Paint _ringPaint = new(PaintFlags.AntiAlias) { Color = Color.White, StrokeWidth = 4 };
    private readonly Paint _textPaint = new(PaintFlags.AntiAlias) { Color = Color.White, TextSize = 34, FakeBoldText = true };
    private readonly Paint _labelBackground = new(PaintFlags.AntiAlias) { Color = Color.Argb(190, 20, 20, 20) };

    // Mode nuit : carte en niveaux de rouge, assombrie.
    private static readonly ColorMatrix NightMatrix = new([
        0.12f, 0.24f, 0.04f, 0, 0,
        0, 0, 0, 0, 0,
        0, 0, 0, 0, 0,
        0, 0, 0, 1, 0,
    ]);

    private readonly Paint _nightBitmapPaint = new() { };
    private bool _nightMode;

    /// <summary>Mode nuit : carte rouge sombre, repères en rouge.</summary>
    public bool NightMode
    {
        get => _nightMode;
        set
        {
            _nightMode = value;
            _nightBitmapPaint.SetColorFilter(value ? new ColorMatrixColorFilter(NightMatrix) : null);
            _ownPaint.Color = value ? Color.Rgb(255, 60, 60) : Color.Rgb(21, 101, 192);
            _allyPaint.Color = value ? Color.Rgb(120, 0, 0) : Color.Rgb(46, 125, 50);
            _ringPaint.Color = value ? Color.Rgb(200, 30, 30) : Color.White;
            _textPaint.Color = value ? Color.Rgb(200, 30, 30) : Color.White;
            _labelBackground.Color = value ? Color.Argb(220, 0, 0, 0) : Color.Argb(190, 20, 20, 20);
            Invalidate();
        }
    }

    private Bitmap? _bitmap;
    private MapInfo? _map;
    private GeoPoint? _own;
    private IReadOnlyList<AllyPosition> _allies = [];
    private GeoPoint? _target;
    private IReadOnlyList<PoiInfo> _points = [];
    private readonly Paint _outlinePaint = new(PaintFlags.AntiAlias) { StrokeWidth = 5 };
    private readonly Paint _poiPaint = new(PaintFlags.AntiAlias);

    public void Update(Bitmap? bitmap, MapInfo? map, GeoPoint? own, IReadOnlyList<AllyPosition> allies, GeoPoint? target,
        IReadOnlyList<PoiInfo> points)
    {
        _bitmap = bitmap;
        (_map, _own, _allies, _target, _points) = (map, own, allies, target, points);
        Invalidate();
    }

    private Color PoiColor(PoiInfo poi)
    {
        if (_nightMode)
            return Color.Rgb(170, 20, 20);
        try
        {
            return Color.ParseColor(poi.Color);
        }
        catch (Java.Lang.IllegalArgumentException)
        {
            return Color.Rgb(249, 168, 37);
        }
    }

    protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec)
    {
        // Hauteur proportionnelle à la carte (carrée par défaut).
        var width = MeasureSpec.GetSize(widthMeasureSpec);
        var ratio = _bitmap is { Width: > 0 } b ? (double)b.Height / b.Width : 1;
        SetMeasuredDimension(width, (int)(width * ratio));
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        if (_bitmap is null || _map is null)
        {
            canvas.DrawColor(_nightMode ? Color.Black : Color.Rgb(43, 47, 51));
            canvas.DrawText(L.T("carte_en_cours_de_telechargement"), 24, 60, _textPaint);
            return;
        }

        canvas.DrawBitmap(_bitmap, null, new Rect(0, 0, Width, Height), _nightMode ? _nightBitmapPaint : null);
        var bounds = new GeoBounds(_map.North, _map.South, _map.West, _map.East);
        PointF ToScreen(GeoPoint p)
        {
            var (x, y) = bounds.ToRelative(p);
            return new PointF((float)(x * Width), (float)(y * Height));
        }

        // Points d'intérêt communiqués par l'orga : contour des zones, repère et nom.
        foreach (var poi in _points)
        {
            var color = PoiColor(poi);
            if (poi.Outline.Count >= 3)
            {
                using var path = new Android.Graphics.Path();
                var first = ToScreen(new GeoPoint(poi.Outline[0].Latitude, poi.Outline[0].Longitude));
                path.MoveTo(first.X, first.Y);
                foreach (var vertex in poi.Outline.Skip(1))
                {
                    var v = ToScreen(new GeoPoint(vertex.Latitude, vertex.Longitude));
                    path.LineTo(v.X, v.Y);
                }

                path.Close();
                _outlinePaint.Color = color;
                _outlinePaint.SetStyle(Paint.Style.Stroke);
                canvas.DrawPath(path, _outlinePaint);
            }

            var p = ToScreen(new GeoPoint(poi.Latitude, poi.Longitude));
            SymbolPainter.Draw(canvas, p.X, p.Y, 40, AirsoftPlanner.Core.Symbols.MilitarySymbols.Draw(poi.Military, poi.Echelon),
                poi.SymbolColor.Length > 0 ? poi.SymbolColor : poi.Color, _nightMode);
            Label(canvas, poi.Name, p.X, p.Y - 40);
        }

        if (_target is { } target)
        {
            var t = ToScreen(target);
            _targetPaint.SetStyle(Paint.Style.Stroke);
            canvas.DrawCircle(t.X, t.Y, 34, _targetPaint);
            Label(canvas, L.T("mission_2"), t.X, t.Y - 46);
        }

        foreach (var ally in _allies)
        {
            var a = ToScreen(new GeoPoint(ally.Latitude, ally.Longitude));
            if (ally.FactionColor.Length > 0)
                SymbolPainter.Draw(canvas, a.X, a.Y, 40, AirsoftPlanner.Core.Symbols.MilitarySymbols.Draw(ally.Military, ally.Echelon), ally.FactionColor, _nightMode);
            else
                Dot(canvas, a, _allyPaint);
            Label(canvas, ally.Team, a.X, a.Y - 40);
        }

        if (_own is { } own)
        {
            var o = ToScreen(own);
            Dot(canvas, o, _ownPaint);
            Label(canvas, Prefs.Team.Length > 0 ? Prefs.Team : L.T("vous"), o.X, o.Y - 34);
        }
    }

    private void Dot(Canvas canvas, PointF p, Paint fill)
    {
        canvas.DrawCircle(p.X, p.Y, 18, fill);
        _ringPaint.SetStyle(Paint.Style.Stroke);
        canvas.DrawCircle(p.X, p.Y, 18, _ringPaint);
    }

    private void Label(Canvas canvas, string text, float centerX, float baselineY)
    {
        var width = _textPaint.MeasureText(text);
        canvas.DrawRoundRect(centerX - width / 2 - 10, baselineY - 34, centerX + width / 2 + 10, baselineY + 10, 10, 10, _labelBackground);
        canvas.DrawText(text, centerX - width / 2, baselineY, _textPaint);
    }
}
