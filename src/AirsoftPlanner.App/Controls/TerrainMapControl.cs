using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using AirsoftPlanner.App.ViewModels;
using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Core.Geo;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using AirsoftPlanner.Core.Localization;

namespace AirsoftPlanner.App.Controls;

/// <summary>
/// Carte du terrain hors ligne : fond calé en GPS, zones, quadrillage (UTM par fuseau, ou degrés).
/// Molette = zoom, glisser = déplacer, clic = sélectionner ou ajouter un sommet en mode tracé,
/// glisser un sommet de la zone sélectionnée = le déplacer, double-clic = vue d'ensemble.
/// </summary>
public class TerrainMapControl : Control
{
    public static readonly StyledProperty<GeoBounds?> ViewBoundsProperty =
        AvaloniaProperty.Register<TerrainMapControl, GeoBounds?>(nameof(ViewBounds));

    public static readonly StyledProperty<MapLayerViewModel?> LayerProperty =
        AvaloniaProperty.Register<TerrainMapControl, MapLayerViewModel?>(nameof(Layer));

    public static readonly StyledProperty<GeoBounds?> AreaProperty =
        AvaloniaProperty.Register<TerrainMapControl, GeoBounds?>(nameof(Area));

    public static readonly StyledProperty<IEnumerable?> ZonesProperty =
        AvaloniaProperty.Register<TerrainMapControl, IEnumerable?>(nameof(Zones));

    public static readonly StyledProperty<ZoneViewModel?> SelectedZoneProperty =
        AvaloniaProperty.Register<TerrainMapControl, ZoneViewModel?>(nameof(SelectedZone), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<bool> IsDrawingProperty =
        AvaloniaProperty.Register<TerrainMapControl, bool>(nameof(IsDrawing));

    public static readonly StyledProperty<bool> ShowUtmGridProperty =
        AvaloniaProperty.Register<TerrainMapControl, bool>(nameof(ShowUtmGrid));

    public static readonly StyledProperty<GeoPoint?> PointerPositionProperty =
        AvaloniaProperty.Register<TerrainMapControl, GeoPoint?>(nameof(PointerPosition), defaultBindingMode: BindingMode.OneWayToSource);

    public static readonly StyledProperty<ICommand?> MapClickCommandProperty =
        AvaloniaProperty.Register<TerrainMapControl, ICommand?>(nameof(MapClickCommand));

    public static readonly StyledProperty<IEnumerable<TeamMarker>?> MarkersProperty =
        AvaloniaProperty.Register<TerrainMapControl, IEnumerable<TeamMarker>?>(nameof(Markers));

    public static readonly StyledProperty<IEnumerable<TeamTrail>?> TrailsProperty =
        AvaloniaProperty.Register<TerrainMapControl, IEnumerable<TeamTrail>?>(nameof(Trails));

    public static readonly StyledProperty<IEnumerable<ItemMarker>?> ItemMarkersProperty =
        AvaloniaProperty.Register<TerrainMapControl, IEnumerable<ItemMarker>?>(nameof(ItemMarkers));

    /// <summary>Faux en suivi d'OP : les zones ne peuvent pas être sélectionnées ni modifiées.</summary>
    public static readonly StyledProperty<bool> AllowEditingProperty =
        AvaloniaProperty.Register<TerrainMapControl, bool>(nameof(AllowEditing), true);

    private const double ClickTolerance = 4;
    private const double HitTolerance = 10;
    private static readonly IBrush EmptyBackground = new SolidColorBrush(Color.FromRgb(0x2B, 0x2F, 0x33));
    private static readonly IPen GridPen = new Pen(new SolidColorBrush(Color.FromArgb(0xB0, 0x00, 0x00, 0x00)), 1);
    private static readonly IPen GridHaloPen = new Pen(new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF)), 3);
    private static readonly IPen ZoneBoundaryPen = new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0xD6, 0x00)), 2.5, new DashStyle([6, 3], 0));
    private static readonly IPen ZoneBoundaryHaloPen = new Pen(new SolidColorBrush(Color.FromArgb(0x90, 0x00, 0x00, 0x00)), 5);
    private static readonly IPen AreaPen = new Pen(Brushes.White, 1.5, new DashStyle([6, 4], 0));
    private static readonly IBrush LabelBackground = new SolidColorBrush(Color.FromArgb(0xC0, 0x10, 0x10, 0x10));
    private static readonly Typeface LabelTypeface = new(FontFamily.Default, weight: FontWeight.SemiBold);

    private readonly HashSet<INotifyPropertyChanged> _observedZones = [];
    private INotifyCollectionChanged? _observedCollection;

    private double _zoom = 1;
    private Vector _pan;
    private Point? _pressPosition;
    private Vector _panAtPress;
    private bool _isPanning;
    private int? _draggedVertex;

    static TerrainMapControl()
    {
        AffectsRender<TerrainMapControl>(LayerProperty, AreaProperty, SelectedZoneProperty, IsDrawingProperty, ShowUtmGridProperty,
            MarkersProperty, TrailsProperty, ItemMarkersProperty);
        FocusableProperty.OverrideDefaultValue<TerrainMapControl>(true);
    }

    public TerrainMapControl()
    {
        ClipToBounds = true;
    }

    public GeoBounds? ViewBounds { get => GetValue(ViewBoundsProperty); set => SetValue(ViewBoundsProperty, value); }

    public MapLayerViewModel? Layer { get => GetValue(LayerProperty); set => SetValue(LayerProperty, value); }

    public GeoBounds? Area { get => GetValue(AreaProperty); set => SetValue(AreaProperty, value); }

    public IEnumerable? Zones { get => GetValue(ZonesProperty); set => SetValue(ZonesProperty, value); }

    public ZoneViewModel? SelectedZone { get => GetValue(SelectedZoneProperty); set => SetValue(SelectedZoneProperty, value); }

    public bool IsDrawing { get => GetValue(IsDrawingProperty); set => SetValue(IsDrawingProperty, value); }

    public bool ShowUtmGrid { get => GetValue(ShowUtmGridProperty); set => SetValue(ShowUtmGridProperty, value); }

    public GeoPoint? PointerPosition { get => GetValue(PointerPositionProperty); set => SetValue(PointerPositionProperty, value); }

    public ICommand? MapClickCommand { get => GetValue(MapClickCommandProperty); set => SetValue(MapClickCommandProperty, value); }

    public IEnumerable<TeamMarker>? Markers { get => GetValue(MarkersProperty); set => SetValue(MarkersProperty, value); }

    public bool AllowEditing { get => GetValue(AllowEditingProperty); set => SetValue(AllowEditingProperty, value); }

    public IEnumerable<TeamTrail>? Trails { get => GetValue(TrailsProperty); set => SetValue(TrailsProperty, value); }

    public IEnumerable<ItemMarker>? ItemMarkers { get => GetValue(ItemMarkersProperty); set => SetValue(ItemMarkersProperty, value); }

    private IEnumerable<ZoneViewModel> ZoneItems => Zones?.OfType<ZoneViewModel>() ?? [];

    /// <summary>Revient à la vue d'ensemble.</summary>
    public void ResetView()
    {
        _zoom = 1;
        _pan = default;
        InvalidateVisual();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AppSettings.CoordinateFormatChanged += InvalidateVisual;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        AppSettings.CoordinateFormatChanged -= InvalidateVisual;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ViewBoundsProperty)
        {
            // Ne recadre que si l'emprise change vraiment (pas à chaque ajout de sommet).
            if (!Equals(change.OldValue, change.NewValue))
                ResetView();
        }
        else if (change.Property == ZonesProperty)
        {
            ObserveZones(change.NewValue as IEnumerable);
            InvalidateVisual();
        }
        else if (change.Property == IsDrawingProperty)
        {
            Cursor = IsDrawing ? new Cursor(StandardCursorType.Cross) : Cursor.Default;
        }
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(EmptyBackground, new Rect(Bounds.Size));
        if (ViewBounds is not { IsValid: true } view)
        {
            DrawCenteredMessage(context, L.T("definissez_l_emprise_du_terrain_ou_telechargez_u"));
            return;
        }

        if (Layer is { } layer)
        {
            var bitmap = layer.Bitmap;
            context.DrawImage(bitmap, new Rect(bitmap.Size), ToScreenRect(view, layer.Bounds));
        }

        if (ShowUtmGrid)
            DrawGrid(context, view);

        if (Area is { IsValid: true } area && Layer is null)
            context.DrawRectangle(null, AreaPen, ToScreenRect(view, area));

        foreach (var zone in ZoneItems.Where(z => z.IsArea))
            DrawArea(context, view, zone);
        foreach (var zone in ZoneItems.Where(z => !z.IsArea))
            DrawPoint(context, view, zone);
        foreach (var zone in ZoneItems.Where(z => z.Points.Count > 0))
            DrawLabel(context, ToScreen(view, Centroid(zone.Points)) + new Vector(0, zone.IsArea ? 0 : zone.ResolvedSymbol == Core.Symbols.MilSymbol.Dot ? -18 : -30), zone.DisplayName);

        foreach (var trail in Trails ?? [])
            DrawTrail(context, view, trail);
        foreach (var item in ItemMarkers ?? [])
            DrawItem(context, view, item);
        foreach (var marker in (Markers ?? []).OrderBy(m => m.IsSelected))
            DrawMarker(context, view, marker);

        if (Layer is { Attribution.Length: > 0 })
            DrawText(context, new Point(Bounds.Width - 6, Bounds.Height - 6), "© " + Layer.Attribution, 10, alignRight: true, alignBottom: true);
    }

    // ----- Interactions -----

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var cursor = e.GetPosition(this);
        var newZoom = Math.Clamp(_zoom * Math.Pow(1.25, e.Delta.Y), 0.5, 200);
        _pan = (Vector)cursor - ((Vector)cursor - _pan) * (newZoom / _zoom);
        _zoom = newZoom;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (ViewBounds is not { IsValid: true } view)
            return;

        var position = e.GetPosition(this);
        if (e.ClickCount == 2)
        {
            ResetView();
            e.Handled = true;
            return;
        }

        _pressPosition = position;
        _panAtPress = _pan;
        _isPanning = false;
        _draggedVertex = IsDrawing || !AllowEditing ? null : FindVertex(view, position);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (ViewBounds is not { IsValid: true } view)
            return;

        var position = e.GetPosition(this);
        PointerPosition = ToGeo(view, position);

        if (_pressPosition is not { } press)
            return;

        if (_draggedVertex is { } vertex && SelectedZone is { } zone)
        {
            zone.MovePoint(vertex, ToGeo(view, position));
            InvalidateVisual();
            return;
        }

        if (!_isPanning && Distance(position, press) > ClickTolerance)
            _isPanning = true;
        if (_isPanning)
        {
            _pan = _panAtPress + (position - press);
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var wasClick = _pressPosition is not null && !_isPanning && _draggedVertex is null;
        _pressPosition = null;
        _isPanning = false;
        _draggedVertex = null;
        e.Pointer.Capture(null);

        if (!wasClick || ViewBounds is not { IsValid: true } view || e.InitialPressMouseButton != MouseButton.Left)
            return;

        var position = e.GetPosition(this);
        if (IsDrawing)
        {
            var point = ToGeo(view, position);
            if (MapClickCommand?.CanExecute(point) == true)
                MapClickCommand.Execute(point);
        }
        else if (AllowEditing)
        {
            SelectedZone = HitTest(view, position);
        }

        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        PointerPosition = null;
    }

    // ----- Projection -----

    /// <summary>Rectangle de la vue d'ensemble, aux proportions réelles du terrain.</summary>
    private Rect BaseRect(GeoBounds view)
    {
        const double margin = 8;
        var (widthMeters, heightMeters) = view.SizeInMeters();
        var available = new Size(Math.Max(1, Bounds.Width - 2 * margin), Math.Max(1, Bounds.Height - 2 * margin));
        var scale = Math.Min(available.Width / widthMeters, available.Height / heightMeters);
        var size = new Size(widthMeters * scale, heightMeters * scale);
        return new Rect(new Point((Bounds.Width - size.Width) / 2, (Bounds.Height - size.Height) / 2), size);
    }

    private Point ToScreen(GeoBounds view, GeoPoint point)
    {
        var rect = BaseRect(view);
        var (x, y) = view.ToRelative(point);
        var world = new Point(rect.X + x * rect.Width, rect.Y + y * rect.Height);
        return new Point(world.X * _zoom + _pan.X, world.Y * _zoom + _pan.Y);
    }

    private GeoPoint ToGeo(GeoBounds view, Point screen)
    {
        var rect = BaseRect(view);
        var world = new Point((screen.X - _pan.X) / _zoom, (screen.Y - _pan.Y) / _zoom);
        return view.FromRelative((world.X - rect.X) / rect.Width, (world.Y - rect.Y) / rect.Height);
    }

    private Rect ToScreenRect(GeoBounds view, GeoBounds bounds) =>
        new(ToScreen(view, new GeoPoint(bounds.North, bounds.West)), ToScreen(view, new GeoPoint(bounds.South, bounds.East)));

    // ----- Dessin -----

    private void DrawArea(DrawingContext context, GeoBounds view, ZoneViewModel zone)
    {
        if (zone.Points.Count == 0)
            return;

        var color = ParseColor(zone.Color);
        var selected = zone == SelectedZone;
        var screen = zone.Points.Select(p => ToScreen(view, p)).ToList();
        var geometry = new StreamGeometry();
        using (var geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(screen[0], zone.IsComplete);
            foreach (var point in screen.Skip(1))
                geometryContext.LineTo(point);
            geometryContext.EndFigure(zone.IsComplete);
        }

        var fill = zone.IsComplete ? new SolidColorBrush(color, selected ? 0.45 : 0.3) : null;
        context.DrawGeometry(fill, new Pen(new SolidColorBrush(color), selected ? 3 : 2), geometry);

        if (selected)
        {
            foreach (var point in screen)
                context.DrawRectangle(Brushes.White, new Pen(new SolidColorBrush(color), 2),
                    new Rect(point.X - 4, point.Y - 4, 8, 8));
        }
    }

    private void DrawPoint(DrawingContext context, GeoBounds view, ZoneViewModel zone)
    {
        if (zone.Points.Count == 0)
            return;

        var center = ToScreen(view, zone.Points[0]);
        var symbol = zone.ResolvedSymbol;
        if (symbol != Core.Symbols.MilSymbol.Dot)
        {
            SymbolRenderer.Draw(context, center, zone == SelectedZone ? 26 : 22, Core.Symbols.MilitarySymbols.Draw(symbol, zone.Model.Echelon),
                zone.SymbolColor, zone == SelectedZone ? Brushes.White : null);
            return;
        }

        var radius = zone == SelectedZone ? 9 : 7;
        context.DrawEllipse(new SolidColorBrush(ParseColor(zone.Color)), new Pen(Brushes.White, 2), center, radius, radius);
    }

    /// <summary>Parcours d'une équipe : ligne reliant ses positions successives, un point par position reçue.</summary>
    private void DrawTrail(DrawingContext context, GeoBounds view, TeamTrail trail)
    {
        if (trail.Points.Count < 2)
            return;

        var brush = new SolidColorBrush(ParseColor(trail.Color), 0.85);
        var points = trail.Points.Select(p => ToScreen(view, p)).ToList();
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(points[0], false);
            foreach (var point in points.Skip(1))
                g.LineTo(point);
            g.EndFigure(false);
        }

        context.DrawGeometry(null, new Pen(Brushes.White, 5, lineJoin: PenLineJoin.Round), geometry);
        context.DrawGeometry(null, new Pen(brush, 3, lineJoin: PenLineJoin.Round), geometry);
        foreach (var point in points.Take(points.Count - 1))
            context.DrawEllipse(brush, new Pen(Brushes.White, 1), point, 3, 3);
    }

    /// <summary>Objet d'objectif : losange doré, avec son nom (et son détenteur).</summary>
    private void DrawItem(DrawingContext context, GeoBounds view, ItemMarker item)
    {
        var c = ToScreen(view, item.Point);
        if (item.SymbolColor is { } color)
        {
            // Véhicule mis en jeu : symbole militaire aux couleurs de la faction.
            SymbolRenderer.Draw(context, c, 20, Core.Symbols.MilitarySymbols.Draw(Core.Symbols.MilSymbol.WheeledVehicle), color);
            DrawLabel(context, c + new Vector(0, -24), item.Label);
            return;
        }

        var size = item.IsSelected ? 10 : 8;
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(c.X, c.Y - size), true);
            g.LineTo(new Point(c.X + size, c.Y));
            g.LineTo(new Point(c.X, c.Y + size));
            g.LineTo(new Point(c.X - size, c.Y));
            g.EndFigure(true);
        }

        context.DrawGeometry(new SolidColorBrush(Color.FromRgb(0xFF, 0xC4, 0x00)), new Pen(Brushes.Black, item.IsSelected ? 2.5 : 1.5), geometry);
        DrawLabel(context, c + new Vector(0, -size - 12), item.Label);
    }

    private void DrawMarker(DrawingContext context, GeoBounds view, TeamMarker marker)
    {
        var center = ToScreen(view, marker.Point);
        var status = new SolidColorBrush(ParseColor(marker.StatusColor));
        if (marker.Target is { } target)
        {
            // Trajet restant vers la zone de la mission, dans la couleur de l'état (juste / en retard).
            var pen = new Pen(status, 2.5, new DashStyle([4, 3], 0));
            context.DrawLine(pen, center, ToScreen(view, target));
        }

        // Symbole militaire de l'équipe aux couleurs de sa faction, entouré de la couleur de son état.
        var height = marker.IsSelected ? 26 : 22;
        SymbolRenderer.Draw(context, center, height, Core.Symbols.MilitarySymbols.Draw(marker.Symbol, marker.Echelon), marker.Color, status);
        DrawLabel(context, center + new Vector(0, height / 2.0 + 14), marker.Label);
    }

    /// <summary>Quadrillage dans le format de coordonnées affiché : UTM (un quadrillage par fuseau) ou degrés.</summary>
    private void DrawGrid(DrawingContext context, GeoBounds view)
    {
        var topLeft = ToGeo(view, new Point(0, 0));
        var bottomRight = ToGeo(view, new Point(Bounds.Width, Bounds.Height));
        if (!topLeft.IsValid || !bottomRight.IsValid || Bounds.Width < 1 || Bounds.Height < 1)
            return;

        if (AppSettings.Current.CoordinateFormat == CoordinateFormat.Utm)
            DrawUtmGrids(context, view, topLeft, bottomRight);
        else
            DrawGraticule(context, view, topLeft, bottomRight, AppSettings.Current.CoordinateFormat == CoordinateFormat.DegreesMinutesSeconds);
    }

    /// <summary>
    /// Terrain à cheval sur deux fuseaux UTM : chaque fuseau a son propre quadrillage (orientations différentes),
    /// limité à sa bande de longitude ; la limite entre fuseaux est tracée en jaune.
    /// </summary>
    private void DrawUtmGrids(DrawingContext context, GeoBounds view, GeoPoint topLeft, GeoPoint bottomRight)
    {
        var midLatitude = (topLeft.Latitude + bottomRight.Latitude) / 2;
        static int StandardZone(double longitude) => Math.Clamp((int)Math.Floor((longitude + 180) / 6) + 1, 1, 60);
        static double ZoneWest(int zone) => -180 + (zone - 1) * 6.0;
        var firstZone = StandardZone(topLeft.Longitude);
        var lastZone = StandardZone(bottomRight.Longitude);

        // Pas du quadrillage commun à tous les fuseaux : environ une ligne tous les 80 pixels.
        var left = UtmCoordinate.FromGeo(new GeoPoint(midLatitude, topLeft.Longitude), firstZone);
        var right = UtmCoordinate.FromGeo(new GeoPoint(midLatitude, bottomRight.Longitude), firstZone);
        var metersPerPixel = Math.Abs(right.Easting - left.Easting) / Bounds.Width;
        var spacing = new[] { 10.0, 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000, 50000, 100000 }
            .FirstOrDefault(s => s / metersPerPixel >= 80, 100000);

        var zones = new List<string>();
        for (var zone = firstZone; zone <= lastZone; zone++)
        {
            var x0 = Math.Max(0, ToScreen(view, new GeoPoint(midLatitude, ZoneWest(zone))).X);
            var x1 = Math.Min(Bounds.Width, ToScreen(view, new GeoPoint(midLatitude, ZoneWest(zone + 1))).X);
            if (x1 - x0 < 1)
                continue;
            using (context.PushClip(new Rect(x0, 0, x1 - x0, Bounds.Height)))
                zones.Add(DrawUtmZoneGrid(context, view, zone, x0, x1, spacing));
        }

        for (var zone = firstZone + 1; zone <= lastZone; zone++)
        {
            var x = ToScreen(view, new GeoPoint(midLatitude, ZoneWest(zone))).X;
            context.DrawLine(ZoneBoundaryHaloPen, new Point(x, 0), new Point(x, Bounds.Height));
            context.DrawLine(ZoneBoundaryPen, new Point(x, 0), new Point(x, Bounds.Height));
            DrawText(context, new Point(x, 20), $"◄ {zone - 1} | {zone} ►", 11);
        }

        DrawText(context, new Point(6, Bounds.Height - 6),
            L.F("quadrillage_utm_x_x", string.Join(" | ", zones), (spacing >= 1000 ? $"{spacing / 1000:0} km" : $"{spacing:0} m"))
            + (zones.Count > 1 ? L.T("limite_de_fuseau_en_jaune") : ""), 10, alignBottom: true);
    }

    /// <returns>Nom du fuseau (ex. « 31U »).</returns>
    private string DrawUtmZoneGrid(DrawingContext context, GeoBounds view, int zone, double x0, double x1, double spacing)
    {
        var corners = new[] { new Point(x0, 0), new Point(x1, 0), new Point(x0, Bounds.Height), new Point(x1, Bounds.Height) }
            .Select(p => UtmCoordinate.FromGeo(ToGeo(view, p), zone)).ToList();
        var minE = corners.Min(c => c.Easting);
        var maxE = corners.Max(c => c.Easting);
        var minN = corners.Min(c => c.Northing);
        var maxN = corners.Max(c => c.Northing);
        var band = UtmCoordinate.FromGeo(ToGeo(view, new Point((x0 + x1) / 2, Bounds.Height / 2)), zone).Band;
        UtmCoordinate Utm(double easting, double northing) => new(zone, band, easting, northing);

        for (var easting = Math.Ceiling(minE / spacing) * spacing; easting <= maxE; easting += spacing)
        {
            var start = ToScreen(view, Utm(easting, minN).ToGeo());
            var end = ToScreen(view, Utm(easting, maxN).ToGeo());
            DrawGridLine(context, start, end);
            // Étiquette là où la ligne croise le haut de la carte.
            var topX = start.X + (end.X - start.X) * (start.Y / Math.Max(1, start.Y - end.Y));
            if (topX >= x0 && topX <= x1)
                DrawText(context, new Point(topX + 3, 2), GridLabel(easting, spacing), 10);
        }

        for (var northing = Math.Ceiling(minN / spacing) * spacing; northing <= maxN; northing += spacing)
        {
            var start = ToScreen(view, Utm(minE, northing).ToGeo());
            var end = ToScreen(view, Utm(maxE, northing).ToGeo());
            DrawGridLine(context, start, end);
            // Étiquette là où la ligne croise le bord gauche du fuseau visible.
            var leftY = start.Y + (end.Y - start.Y) * ((x0 - start.X) / Math.Max(1, end.X - start.X));
            DrawText(context, new Point(x0 + 3, leftY - 14), GridLabel(northing, spacing), 10);
        }

        return $"{zone}{band}";
    }

    /// <summary>Méridiens et parallèles, étiquetés en degrés décimaux ou en degrés-minutes-secondes.</summary>
    private void DrawGraticule(DrawingContext context, GeoBounds view, GeoPoint topLeft, GeoPoint bottomRight, bool dms)
    {
        var (west, east) = (topLeft.Longitude, bottomRight.Longitude);
        var (south, north) = (bottomRight.Latitude, topLeft.Latitude);
        var pixelsPerDegree = Bounds.Width / Math.Max(1e-9, east - west);
        double[] candidates = dms
            ? [1 / 3600.0, 2 / 3600.0, 5 / 3600.0, 10 / 3600.0, 15 / 3600.0, 30 / 3600.0, 1 / 60.0, 2 / 60.0, 5 / 60.0, 10 / 60.0, 15 / 60.0, 30 / 60.0, 1, 2, 5]
            : [0.00001, 0.00002, 0.00005, 0.0001, 0.0002, 0.0005, 0.001, 0.002, 0.005, 0.01, 0.02, 0.05, 0.1, 0.2, 0.5, 1, 2, 5];
        var spacing = candidates.FirstOrDefault(c => c * pixelsPerDegree >= 90, 5);

        for (var k = Math.Ceiling(west / spacing); k * spacing <= east; k++)
        {
            var longitude = k * spacing;
            var x = ToScreen(view, new GeoPoint(north, longitude)).X;
            DrawGridLine(context, new Point(x, 0), new Point(x, Bounds.Height));
            DrawText(context, new Point(x + 3, 2), AngleLabel(longitude, spacing, dms, 'E', 'O'), 10);
        }

        for (var k = Math.Ceiling(south / spacing); k * spacing <= north; k++)
        {
            var latitude = k * spacing;
            var y = ToScreen(view, new GeoPoint(latitude, west)).Y;
            DrawGridLine(context, new Point(0, y), new Point(Bounds.Width, y));
            DrawText(context, new Point(3, y - 14), AngleLabel(latitude, spacing, dms, 'N', 'S'), 10);
        }

        var step = dms
            ? spacing >= 1 ? $"{spacing:0}°" : spacing >= 1 / 60.0 ? $"{Math.Round(spacing * 60):0}'" : $"{Math.Round(spacing * 3600):0}\""
            : AngleNumber(spacing, spacing) + "°";
        DrawText(context, new Point(6, Bounds.Height - 6), L.F("quadrillage_x_x", (dms ? L.T("degres_minutes_secondes") : L.T("degres_decimaux")), step), 10, alignBottom: true);
    }

    private static string AngleLabel(double value, double spacing, bool dms, char positive, char negative)
    {
        var hemisphere = value >= 0 ? positive : negative;
        if (!dms)
            return $"{AngleNumber(Math.Abs(value), spacing)}° {hemisphere}";

        var totalSeconds = (long)Math.Round(Math.Abs(value) * 3600);
        var (degrees, minutes, seconds) = (totalSeconds / 3600, totalSeconds % 3600 / 60, totalSeconds % 60);
        return spacing >= 1 ? $"{degrees}° {hemisphere}"
            : spacing >= 1 / 60.0 ? $"{degrees}°{minutes:00}' {hemisphere}"
            : $"{degrees}°{minutes:00}'{seconds:00}\" {hemisphere}";
    }

    private static string AngleNumber(double value, double spacing)
    {
        var decimals = Math.Clamp((int)Math.Ceiling(-Math.Log10(spacing) - 1e-9), 0, 6);
        return value.ToString("F" + decimals, AirsoftPlanner.Core.Localization.L.Culture);
    }

    private static string GridLabel(double value, double spacing) => spacing >= 1000
        ? (value / 1000).ToString("0", CultureInfo.InvariantCulture)
        : (value / 1000).ToString(spacing >= 100 ? "0.0" : "0.00", AirsoftPlanner.Core.Localization.L.Culture);

    private static void DrawGridLine(DrawingContext context, Point start, Point end)
    {
        context.DrawLine(GridHaloPen, start, end);
        context.DrawLine(GridPen, start, end);
    }

    private void DrawCenteredMessage(DrawingContext context, string message)
    {
        var text = new FormattedText(message, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 14, Brushes.Gainsboro)
        {
            MaxTextWidth = Math.Max(100, Bounds.Width - 80),
            TextAlignment = TextAlignment.Center,
        };
        context.DrawText(text, new Point((Bounds.Width - text.MaxTextWidth) / 2, (Bounds.Height - text.Height) / 2));
    }

    private static void DrawLabel(DrawingContext context, Point center, string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return;

        var text = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelTypeface, 12, Brushes.White);
        var origin = new Point(center.X - text.Width / 2, center.Y - text.Height / 2);
        context.DrawRectangle(LabelBackground, null, new Rect(origin, new Size(text.Width, text.Height)).Inflate(new Thickness(4, 1)), 3, 3);
        context.DrawText(text, origin);
    }

    private static void DrawText(DrawingContext context, Point anchor, string value, double size,
        bool alignRight = false, bool alignBottom = false)
    {
        var text = new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, size, Brushes.White);
        var origin = new Point(alignRight ? anchor.X - text.Width : anchor.X, alignBottom ? anchor.Y - text.Height : anchor.Y);
        context.DrawRectangle(LabelBackground, null, new Rect(origin, new Size(text.Width, text.Height)).Inflate(new Thickness(3, 1)), 2, 2);
        context.DrawText(text, origin);
    }

    // ----- Sélection -----

    private ZoneViewModel? HitTest(GeoBounds view, Point position)
    {
        var points = ZoneItems.Where(z => !z.IsArea && z.Points.Count > 0)
            .FirstOrDefault(z => Distance(ToScreen(view, z.Points[0]), position) <= HitTolerance);
        if (points is not null)
            return points;

        // Parmi les zones qui contiennent le clic, la dernière dessinée (au-dessus des autres).
        return ZoneItems.Where(z => z.IsArea && z.IsComplete)
            .LastOrDefault(z => Contains(z.Points.Select(p => ToScreen(view, p)).ToList(), position));
    }

    private int? FindVertex(GeoBounds view, Point position)
    {
        if (SelectedZone is not { IsArea: true } zone)
            return null;

        for (var i = 0; i < zone.Points.Count; i++)
        {
            if (Distance(ToScreen(view, zone.Points[i]), position) <= HitTolerance)
                return i;
        }

        return null;
    }

    private static bool Contains(IReadOnlyList<Point> polygon, Point point)
    {
        var inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            if ((polygon[i].Y > point.Y) != (polygon[j].Y > point.Y)
                && point.X < (polygon[j].X - polygon[i].X) * (point.Y - polygon[i].Y) / (polygon[j].Y - polygon[i].Y) + polygon[i].X)
                inside = !inside;
        }

        return inside;
    }

    // ----- Suivi des zones -----

    private void ObserveZones(IEnumerable? zones)
    {
        if (_observedCollection is not null)
            _observedCollection.CollectionChanged -= OnZonesCollectionChanged;
        _observedCollection = zones as INotifyCollectionChanged;
        if (_observedCollection is not null)
            _observedCollection.CollectionChanged += OnZonesCollectionChanged;
        ResubscribeZoneItems();
    }

    private void OnZonesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ResubscribeZoneItems();
        InvalidateVisual();
    }

    private void ResubscribeZoneItems()
    {
        foreach (var zone in _observedZones)
            zone.PropertyChanged -= OnZoneChanged;
        _observedZones.Clear();
        foreach (var zone in ZoneItems)
        {
            zone.PropertyChanged += OnZoneChanged;
            _observedZones.Add(zone);
        }
    }

    private void OnZoneChanged(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();

    // ----- Utilitaires -----

    private static GeoPoint Centroid(IReadOnlyList<GeoPoint> points) =>
        new(points.Average(p => p.Latitude), points.Average(p => p.Longitude));

    private static double Distance(Point a, Point b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    private static Color ParseColor(string hex) => Color.TryParse(hex, out var color) ? color : Colors.Orange;
}
