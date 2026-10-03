using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Core.Gps;
using Android.Content;
using Android.Graphics;
using Android.Views;

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

    private Bitmap? _bitmap;
    private MapInfo? _map;
    private GeoPoint? _own;
    private IReadOnlyList<AllyPosition> _allies = [];
    private GeoPoint? _target;

    public void Update(Bitmap? bitmap, MapInfo? map, GeoPoint? own, IReadOnlyList<AllyPosition> allies, GeoPoint? target)
    {
        _bitmap = bitmap;
        (_map, _own, _allies, _target) = (map, own, allies, target);
        Invalidate();
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
            canvas.DrawColor(Color.Rgb(43, 47, 51));
            canvas.DrawText("Carte en cours de téléchargement…", 24, 60, _textPaint);
            return;
        }

        canvas.DrawBitmap(_bitmap, null, new Rect(0, 0, Width, Height), null);
        var bounds = new GeoBounds(_map.North, _map.South, _map.West, _map.East);
        PointF ToScreen(GeoPoint p)
        {
            var (x, y) = bounds.ToRelative(p);
            return new PointF((float)(x * Width), (float)(y * Height));
        }

        if (_target is { } target)
        {
            var t = ToScreen(target);
            _targetPaint.SetStyle(Paint.Style.Stroke);
            canvas.DrawCircle(t.X, t.Y, 34, _targetPaint);
            Label(canvas, "Mission", t.X, t.Y - 46);
        }

        foreach (var ally in _allies)
        {
            var a = ToScreen(new GeoPoint(ally.Latitude, ally.Longitude));
            Dot(canvas, a, _allyPaint);
            Label(canvas, ally.Team, a.X, a.Y - 34);
        }

        if (_own is { } own)
        {
            var o = ToScreen(own);
            Dot(canvas, o, _ownPaint);
            Label(canvas, "Vous", o.X, o.Y - 34);
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
