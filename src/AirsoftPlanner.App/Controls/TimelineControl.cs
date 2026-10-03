using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using AirsoftPlanner.App.ViewModels;
using AirsoftPlanner.Core.Planning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;

namespace AirsoftPlanner.App.Controls;

/// <summary>
/// Frise verticale du scénario : une colonne par équipe, le temps qui descend.
/// Glisser une mission = la déplacer (et changer d'équipe en changeant de colonne),
/// glisser son bord bas = changer sa durée, double-clic sur une case vide = nouvelle mission,
/// molette = défiler, Maj + molette = défiler horizontalement, Ctrl + molette = zoomer.
/// </summary>
public class TimelineControl : Control
{
    public static readonly StyledProperty<IEnumerable?> ColumnsProperty =
        AvaloniaProperty.Register<TimelineControl, IEnumerable?>(nameof(Columns));

    public static readonly StyledProperty<IEnumerable?> MissionsProperty =
        AvaloniaProperty.Register<TimelineControl, IEnumerable?>(nameof(Missions));

    public static readonly StyledProperty<MissionViewModel?> SelectedMissionProperty =
        AvaloniaProperty.Register<TimelineControl, MissionViewModel?>(nameof(SelectedMission), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<int> OperationStartProperty =
        AvaloniaProperty.Register<TimelineControl, int>(nameof(OperationStart), 9 * 60);

    public static readonly StyledProperty<int> OperationEndProperty =
        AvaloniaProperty.Register<TimelineControl, int>(nameof(OperationEnd), 18 * 60);

    public static readonly StyledProperty<ICommand?> CreateCommandProperty =
        AvaloniaProperty.Register<TimelineControl, ICommand?>(nameof(CreateCommand));

    /// <summary>Instant suivi (ligne « maintenant »), ou NaN pour ne pas l'afficher.</summary>
    public static readonly StyledProperty<double> NowMinutesProperty =
        AvaloniaProperty.Register<TimelineControl, double>(nameof(NowMinutes), double.NaN);

    /// <summary>En suivi : la frise défile pour garder la ligne « maintenant » visible.</summary>
    public static readonly StyledProperty<bool> FollowNowProperty =
        AvaloniaProperty.Register<TimelineControl, bool>(nameof(FollowNow));

    /// <summary>Premier jour de l'OP, pour afficher les dates aux changements de jour.</summary>
    public static readonly StyledProperty<DateTime> DayProperty =
        AvaloniaProperty.Register<TimelineControl, DateTime>(nameof(Day), DateTime.Today);

    private const double HeaderHeight = 46;
    private const double TopPadding = 10;
    private const double GutterWidth = 64;
    private const double MinColumnWidth = 150;
    private const double ResizeHandle = 7;
    private const int Snap = 5;

    private static readonly IBrush Background = new SolidColorBrush(Color.FromRgb(0xF4, 0xF5, 0xF7));
    private static readonly IBrush OutsideOperation = new SolidColorBrush(Color.FromRgb(0xDD, 0xDF, 0xE3));
    private static readonly IBrush HeaderBackground = new SolidColorBrush(Color.FromRgb(0x26, 0x32, 0x38));
    private static readonly IPen HourPen = new Pen(new SolidColorBrush(Color.FromRgb(0xB0, 0xB6, 0xBE)), 1);
    private static readonly IPen HalfHourPen = new Pen(new SolidColorBrush(Color.FromRgb(0xD5, 0xD9, 0xDE)), 1, DashStyle.Dash);
    private static readonly IPen ColumnPen = new Pen(new SolidColorBrush(Color.FromRgb(0xC8, 0xCC, 0xD2)), 1);
    private static readonly IPen SelectedPen = new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0xC4, 0x00)), 3);
    private static readonly IPen IssuePen = new Pen(new SolidColorBrush(Color.FromRgb(0xD3, 0x2F, 0x2F)), 2.5);
    private static readonly IPen DependencyPen = new Pen(new SolidColorBrush(Color.FromArgb(0xA0, 0x37, 0x47, 0x4F)), 1.5);
    private static readonly IPen BrokenDependencyPen = new Pen(new SolidColorBrush(Color.FromRgb(0xD3, 0x2F, 0x2F)), 1.5, DashStyle.Dash);
    private static readonly Typeface Bold = new(FontFamily.Default, weight: FontWeight.SemiBold);

    private readonly List<INotifyPropertyChanged> _observed = [];
    private readonly List<INotifyCollectionChanged> _observedCollections = [];

    private double _minutePixels = 1.5;
    private bool _autoFit = true;
    private double _scrollY;
    private double _scrollX;
    private bool _followPending;

    private DragMode _drag;
    private MissionViewModel? _dragMission;
    private Point _pressPosition;
    private int _pressStart;
    private int _pressDuration;
    private int _pressColumn;
    private bool _dragMoved;

    private enum DragMode { None, Move, Resize, Pan }

    static TimelineControl()
    {
        AffectsRender<TimelineControl>(SelectedMissionProperty, OperationStartProperty, OperationEndProperty, NowMinutesProperty, DayProperty);
        FocusableProperty.OverrideDefaultValue<TimelineControl>(true);
    }

    public TimelineControl()
    {
        ClipToBounds = true;
    }

    public IEnumerable? Columns { get => GetValue(ColumnsProperty); set => SetValue(ColumnsProperty, value); }

    public IEnumerable? Missions { get => GetValue(MissionsProperty); set => SetValue(MissionsProperty, value); }

    public MissionViewModel? SelectedMission { get => GetValue(SelectedMissionProperty); set => SetValue(SelectedMissionProperty, value); }

    public int OperationStart { get => GetValue(OperationStartProperty); set => SetValue(OperationStartProperty, value); }

    public int OperationEnd { get => GetValue(OperationEndProperty); set => SetValue(OperationEndProperty, value); }

    public ICommand? CreateCommand { get => GetValue(CreateCommandProperty); set => SetValue(CreateCommandProperty, value); }

    public double NowMinutes { get => GetValue(NowMinutesProperty); set => SetValue(NowMinutesProperty, value); }

    public DateTime Day { get => GetValue(DayProperty); set => SetValue(DayProperty, value); }

    public bool FollowNow { get => GetValue(FollowNowProperty); set => SetValue(FollowNowProperty, value); }

    private IReadOnlyList<TeamViewModel> ColumnItems => Columns?.OfType<TeamViewModel>().ToList() ?? [];

    private IEnumerable<MissionViewModel> MissionItems => Missions?.OfType<MissionViewModel>() ?? [];

    /// <summary>Affiche toute la durée de l'OP dans la hauteur disponible.</summary>
    public void FitToHeight()
    {
        _autoFit = true;
        _scrollY = 0;
        InvalidateVisual();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ColumnsProperty || change.Property == MissionsProperty)
        {
            Observe();
            InvalidateVisual();
        }
        else if (change.Property == NowMinutesProperty || change.Property == FollowNowProperty || change.Property == BoundsProperty)
        {
            // Le défilement est recalculé au prochain rendu, une fois l'échelle connue.
            _followPending = FollowNow;
            InvalidateVisual();
        }
    }

    // ----- Géométrie -----

    /// <summary>Plage affichée, en minutes : horaires de l'OP élargis aux missions qui débordent, à l'heure près.</summary>
    private (int Start, int End) Range()
    {
        var start = OperationStart;
        var end = Math.Max(OperationEnd, OperationStart + 60);
        foreach (var mission in MissionItems)
        {
            start = Math.Min(start, mission.StartMinutes);
            end = Math.Max(end, mission.EndMinutes);
        }

        return ((int)Math.Floor(start / 60.0) * 60, (int)Math.Ceiling(end / 60.0) * 60);
    }

    private double ColumnWidth(int count) =>
        count == 0 ? 0 : Math.Max(MinColumnWidth, (Bounds.Width - GutterWidth) / count);

    private double YOf(int minutes, int rangeStart) => HeaderHeight + TopPadding + (minutes - rangeStart) * _minutePixels - _scrollY;

    private int MinutesAt(double y, int rangeStart) => rangeStart + (int)Math.Round((y - HeaderHeight - TopPadding + _scrollY) / _minutePixels);

    private double XOfColumn(int index, double width) => GutterWidth + index * width - _scrollX;

    private int ColumnAt(double x, int count, double width) =>
        width <= 0 ? -1 : (int)Math.Floor((x - GutterWidth + _scrollX) / width) is var i && i >= 0 && i < count ? i : -1;

    private Rect BlockRect(MissionViewModel mission, int column, double width, int rangeStart, Lanes lanes)
    {
        var top = YOf(mission.StartMinutes, rangeStart);
        var height = Math.Max(16, mission.EndMinutes * _minutePixels - mission.StartMinutes * _minutePixels);
        var (lane, count) = lanes.TryGetValue((mission, column), out var found) ? found : (0, 1);
        var laneWidth = (width - 8) / count;
        return new Rect(XOfColumn(column, width) + 4 + lane * laneWidth, top, laneWidth - (count > 1 ? 2 : 0), height);
    }

    /// <summary>
    /// Répartit en couloirs les missions qui se chevauchent dans une même colonne, pour qu'aucune
    /// n'en cache une autre : chaque groupe de missions qui se chevauchent partage la largeur de la colonne.
    /// </summary>
    private Lanes ComputeLanes(IReadOnlyList<TeamViewModel> columns)
    {
        var lanes = new Lanes();
        for (var column = 0; column < columns.Count; column++)
        {
            var teamId = columns[column].Model.Id;
            var missions = MissionItems.Where(m => m.TeamIds.Contains(teamId))
                .OrderBy(m => m.StartMinutes).ThenBy(m => m.EndMinutes).ToList();
            var group = new List<(MissionViewModel Mission, int Lane)>();
            var laneEnds = new List<int>();
            var groupEnd = int.MinValue;

            void CloseGroup()
            {
                foreach (var (mission, lane) in group)
                    lanes[(mission, column)] = (lane, laneEnds.Count);
                group.Clear();
                laneEnds.Clear();
            }

            foreach (var mission in missions)
            {
                if (mission.StartMinutes >= groupEnd)
                    CloseGroup();
                var lane = laneEnds.FindIndex(end => end <= mission.StartMinutes);
                if (lane < 0)
                {
                    lane = laneEnds.Count;
                    laneEnds.Add(mission.EndMinutes);
                }
                else
                {
                    laneEnds[lane] = mission.EndMinutes;
                }

                group.Add((mission, lane));
                groupEnd = Math.Max(groupEnd, mission.EndMinutes);
            }

            CloseGroup();
        }

        return lanes;
    }

    private sealed class Lanes : Dictionary<(MissionViewModel Mission, int Column), (int Lane, int Count)>;

    private void UpdateScale((int Start, int End) range)
    {
        var available = Math.Max(100, Bounds.Height - HeaderHeight - TopPadding - 10);
        // Tout afficher si possible, sans descendre sous une échelle lisible (1 h = 60 px) : on défile au-delà.
        if (_autoFit)
            _minutePixels = Math.Clamp(available / Math.Max(60, range.End - range.Start), 1.0, 6);

        var contentHeight = (range.End - range.Start) * _minutePixels;
        _scrollY = Math.Clamp(_scrollY, 0, Math.Max(0, contentHeight - available));
        var columns = ColumnItems.Count;
        var contentWidth = columns * ColumnWidth(columns);
        _scrollX = Math.Clamp(_scrollX, 0, Math.Max(0, contentWidth - (Bounds.Width - GutterWidth)));
    }

    // ----- Dessin -----

    public override void Render(DrawingContext context)
    {
        var columns = ColumnItems;
        var range = Range();
        UpdateScale(range);
        if (_followPending && !double.IsNaN(NowMinutes) && Bounds.Height > HeaderHeight)
        {
            // Garde « maintenant » au premier tiers de la hauteur visible.
            _scrollY = (NowMinutes - range.Start) * _minutePixels + TopPadding - (Bounds.Height - HeaderHeight) / 3;
            _followPending = false;
            UpdateScale(range);
        }

        var width = ColumnWidth(columns.Count);

        context.FillRectangle(Background, new Rect(Bounds.Size));
        if (columns.Count == 0)
        {
            DrawText(context, "Ajoutez des équipes (onglet Équipes) pour construire la frise du scénario.",
                new Point(24, 24), 14, Brushes.DimGray, Bounds.Width - 48);
            return;
        }

        var body = new Rect(GutterWidth, HeaderHeight, Math.Max(0, Bounds.Width - GutterWidth), Math.Max(0, Bounds.Height - HeaderHeight));
        using (context.PushClip(body))
        {
            var lanes = ComputeLanes(columns);
            DrawBackground(context, range, columns.Count, width);
            foreach (var mission in MissionItems.Where(m => m != SelectedMission))
                DrawMission(context, mission, columns, width, range.Start, lanes);
            if (SelectedMission is { } selected)
                DrawMission(context, selected, columns, width, range.Start, lanes);
            DrawDependencies(context, columns, width, range.Start, lanes);
            DrawDayBreaks(context, range);
            DrawNow(context, range.Start);
        }

        DrawGutter(context, range);
        DrawHeader(context, columns, width);
    }

    private void DrawBackground(DrawingContext context, (int Start, int End) range, int columns, double width)
    {
        var top = YOf(range.Start, range.Start);
        var bottom = YOf(range.End, range.Start);
        var opStart = YOf(OperationStart, range.Start);
        var opEnd = YOf(OperationEnd, range.Start);
        context.FillRectangle(OutsideOperation, new Rect(GutterWidth, top, Bounds.Width, Math.Max(0, opStart - top)));
        context.FillRectangle(OutsideOperation, new Rect(GutterWidth, opEnd, Bounds.Width, Math.Max(0, bottom - opEnd)));

        for (var minutes = range.Start; minutes <= range.End; minutes += 30)
        {
            var y = YOf(minutes, range.Start);
            context.DrawLine(minutes % 60 == 0 ? HourPen : HalfHourPen, new Point(GutterWidth, y), new Point(Bounds.Width, y));
        }

        for (var i = 0; i <= columns; i++)
        {
            var x = XOfColumn(i, width);
            context.DrawLine(ColumnPen, new Point(x, HeaderHeight), new Point(x, Bounds.Height));
        }
    }

    private void DrawMission(DrawingContext context, MissionViewModel mission, IReadOnlyList<TeamViewModel> columns, double width, int rangeStart, Lanes lanes)
    {
        var color = Color.TryParse(mission.Color, out var parsed) ? parsed : Colors.SlateGray;
        var opacity = mission.IsEnabled ? 0.92 : 0.3;
        var fill = new SolidColorBrush(color, opacity);
        var textBrush = !mission.IsEnabled ? Brushes.DimGray : IsLight(color) ? Brushes.Black : Brushes.White;
        var border = mission == SelectedMission ? SelectedPen
            : mission.HasIssues && mission.IsEnabled ? IssuePen
            : mission.IsEssential ? new Pen(new SolidColorBrush(Darken(color)), 1.5)
            : new Pen(new SolidColorBrush(Darken(color)), 1.5, DashStyle.Dash);

        foreach (var (team, index) in columns.Select((t, i) => (t, i)).Where(x => mission.TeamIds.Contains(x.t.Model.Id)))
        {
            var rect = BlockRect(mission, index, width, rangeStart, lanes);
            context.DrawRectangle(fill, border, rect, 4, 4);

            var title = (mission.HasIssues && mission.IsEnabled ? "⚠ " : "") + mission.Name
                        + (mission.IsEssential ? "" : " (optionnelle)") + (mission.IsEnabled ? "" : " — désactivée");
            var lines = new List<(string Text, double Size, Typeface Face)>
            {
                ($"{MissionTime.Format(mission.StartMinutes)} – {MissionTime.Format(mission.EndMinutes)}", 10, Typeface.Default),
                (title, 12, Bold),
            };
            if (mission.ZoneName.Length > 0)
                lines.Add(("📍 " + mission.ZoneName, 11, Typeface.Default));

            using (context.PushClip(rect.Deflate(3)))
            {
                var y = rect.Y + 3;
                foreach (var (text, size, face) in lines)
                {
                    if (y > rect.Bottom - 6)
                        break;
                    var formatted = Format(text, size, textBrush, face, rect.Width - 10);
                    context.DrawText(formatted, new Point(rect.X + 6, y));
                    y += formatted.Height;
                }
            }
        }
    }

    private void DrawDependencies(DrawingContext context, IReadOnlyList<TeamViewModel> columns, double width, int rangeStart, Lanes lanes)
    {
        var missions = MissionItems.ToDictionary(m => m.Model.Id);
        foreach (var mission in missions.Values)
        {
            var target = FirstColumn(mission, columns);
            if (target < 0)
                continue;

            foreach (var predecessorId in mission.PredecessorIds)
            {
                if (!missions.TryGetValue(predecessorId, out var predecessor) || FirstColumn(predecessor, columns) is var source && source < 0)
                    continue;

                var from = BlockRect(predecessor, source, width, rangeStart, lanes);
                var to = BlockRect(mission, target, width, rangeStart, lanes);
                var start = new Point(from.Center.X, from.Bottom);
                var end = new Point(to.Center.X, to.Top);
                var broken = !predecessor.IsEnabled || mission.StartMinutes < predecessor.EndMinutes;
                var pen = broken ? BrokenDependencyPen : DependencyPen;
                context.DrawLine(pen, start, end);
                DrawArrowHead(context, pen, start, end);
            }
        }
    }

    private static void DrawArrowHead(DrawingContext context, IPen pen, Point from, Point to)
    {
        var direction = to - from;
        var length = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
        if (length < 1)
            return;

        var unit = direction / length;
        var normal = new Vector(-unit.Y, unit.X);
        context.DrawLine(pen, to, to - unit * 8 + normal * 4);
        context.DrawLine(pen, to, to - unit * 8 - normal * 4);
    }

    private static readonly IBrush NowBrush = new SolidColorBrush(Color.FromRgb(0xD3, 0x2F, 0x2F));
    private static readonly IPen DayBreakPen = new Pen(new SolidColorBrush(Color.FromRgb(0x37, 0x47, 0x4F)), 2);

    private void DrawNow(DrawingContext context, int rangeStart)
    {
        if (double.IsNaN(NowMinutes))
            return;

        var y = HeaderHeight + TopPadding + (NowMinutes - rangeStart) * _minutePixels - _scrollY;
        context.DrawLine(new Pen(NowBrush, 2), new Point(GutterWidth, y), new Point(Bounds.Width, y));
        var label = Format(MissionTime.Format((int)NowMinutes), 11, Brushes.White, Bold, 80);
        var box = new Rect(GutterWidth + 2, y - label.Height - 2, label.Width + 8, label.Height + 2);
        context.DrawRectangle(NowBrush, null, box, 3, 3);
        context.DrawText(label, new Point(box.X + 4, box.Y + 1));
    }

    /// <summary>Trait et date à chaque minuit, pour les OP sur plusieurs jours.</summary>
    private void DrawDayBreaks(DrawingContext context, (int Start, int End) range)
    {
        var french = CultureInfo.GetCultureInfo("fr-FR");
        for (var midnight = (int)Math.Ceiling(range.Start / 1440.0) * 1440; midnight <= range.End; midnight += 1440)
        {
            if (midnight == range.Start)
                continue;
            var y = YOf(midnight, range.Start);
            context.DrawLine(DayBreakPen, new Point(GutterWidth, y), new Point(Bounds.Width, y));
            var label = Format(Day.AddMinutes(midnight).ToString("dddd d MMMM", french), 11, Brushes.White, Bold, 200);
            var box = new Rect(Bounds.Width - label.Width - 14, y + 2, label.Width + 8, label.Height + 2);
            context.DrawRectangle(HeaderBackground, null, box, 3, 3);
            context.DrawText(label, new Point(box.X + 4, box.Y + 1));
        }
    }

    private void DrawGutter(DrawingContext context, (int Start, int End) range)
    {
        var gutter = new Rect(0, HeaderHeight, GutterWidth, Math.Max(0, Bounds.Height - HeaderHeight));
        context.FillRectangle(Brushes.White, gutter);
        context.DrawLine(ColumnPen, new Point(GutterWidth, HeaderHeight), new Point(GutterWidth, Bounds.Height));
        using (context.PushClip(gutter))
        {
            var step = _minutePixels * 30 >= 22 ? 30 : 60;
            for (var minutes = range.Start; minutes <= range.End; minutes += step)
            {
                var text = Format(MissionTime.Format(minutes), minutes % 60 == 0 ? 12 : 10,
                    minutes % 60 == 0 ? Brushes.Black : Brushes.Gray, minutes % 60 == 0 ? Bold : Typeface.Default, GutterWidth);
                context.DrawText(text, new Point(GutterWidth - text.Width - 8, YOf(minutes, range.Start) - text.Height / 2));
            }
        }
    }

    private void DrawHeader(DrawingContext context, IReadOnlyList<TeamViewModel> columns, double width)
    {
        context.FillRectangle(HeaderBackground, new Rect(0, 0, Bounds.Width, HeaderHeight));
        using (context.PushClip(new Rect(GutterWidth, 0, Math.Max(0, Bounds.Width - GutterWidth), HeaderHeight)))
        {
            for (var i = 0; i < columns.Count; i++)
            {
                var team = columns[i];
                var x = XOfColumn(i, width);
                var factionColor = team.Faction is { } faction && Color.TryParse(faction.Color, out var c) ? c : Colors.Gray;
                context.FillRectangle(new SolidColorBrush(factionColor), new Rect(x + 1, 0, width - 2, 5));
                context.DrawText(Format(team.Name, 13, Brushes.White, Bold, width - 12), new Point(x + 8, 9));
                context.DrawText(Format(team.Faction?.Name ?? "Sans faction", 10, Brushes.LightGray, Typeface.Default, width - 12),
                    new Point(x + 8, 27));
            }
        }

        context.DrawText(Format("Heure", 11, Brushes.LightGray, Typeface.Default, GutterWidth), new Point(10, 16));
    }

    private static void DrawText(DrawingContext context, string text, Point origin, double size, IBrush brush, double maxWidth) =>
        context.DrawText(Format(text, size, brush, Typeface.Default, maxWidth), origin);

    private static FormattedText Format(string text, double size, IBrush brush, Typeface face, double maxWidth) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, size, brush)
        {
            MaxTextWidth = Math.Max(1, maxWidth),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };

    // ----- Interactions -----

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var range = Range();
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            // Zoom centré sur l'heure sous le curseur.
            var y = e.GetPosition(this).Y;
            var minutesUnderCursor = (y - HeaderHeight - TopPadding + _scrollY) / _minutePixels;
            _autoFit = false;
            _minutePixels = Math.Clamp(_minutePixels * Math.Pow(1.2, e.Delta.Y), 0.4, 12);
            _scrollY = minutesUnderCursor * _minutePixels - (y - HeaderHeight - TopPadding);
        }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) || Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y))
        {
            _scrollX -= (e.Delta.X != 0 ? e.Delta.X : e.Delta.Y) * 60;
        }
        else
        {
            _scrollY -= e.Delta.Y * 60;
        }

        UpdateScale(range);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var position = e.GetPosition(this);
        var columns = ColumnItems;
        var width = ColumnWidth(columns.Count);
        var range = Range();
        if (position.Y < HeaderHeight || position.X < GutterWidth || columns.Count == 0)
            return;

        var hit = HitTest(position, columns, width, range.Start);
        if (e.ClickCount == 2 && hit is null)
        {
            var column = ColumnAt(position.X, columns.Count, width);
            var minutes = SnapMinutes(MinutesAt(position.Y, range.Start));
            var slot = column >= 0 ? new TimelineSlot(columns[column], minutes) : null;
            if (slot is not null && CreateCommand?.CanExecute(slot) == true)
                CreateCommand.Execute(slot);
            e.Handled = true;
            return;
        }

        _pressPosition = position;
        _dragMoved = false;
        if (hit is { } found)
        {
            SelectedMission = found.Mission;
            _dragMission = found.Mission;
            _drag = found.OnResizeHandle ? DragMode.Resize : DragMode.Move;
            _pressStart = found.Mission.StartMinutes;
            _pressDuration = found.Mission.EndMinutes - found.Mission.StartMinutes;
            _pressColumn = found.Column;
        }
        else
        {
            _drag = DragMode.Pan;
            _pressStart = (int)_scrollY;
            _pressDuration = (int)_scrollX;
        }

        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var position = e.GetPosition(this);
        var columns = ColumnItems;
        var width = ColumnWidth(columns.Count);
        var range = Range();

        if (_drag == DragMode.None)
        {
            var hover = HitTest(position, columns, width, range.Start);
            Cursor = hover is { OnResizeHandle: true } ? new Cursor(StandardCursorType.SizeNorthSouth)
                : hover is not null ? new Cursor(StandardCursorType.Hand)
                : Cursor.Default;
            return;
        }

        var delta = position - _pressPosition;
        if (!_dragMoved && Math.Abs(delta.X) < 3 && Math.Abs(delta.Y) < 3)
            return;
        _dragMoved = true;

        var deltaMinutes = SnapMinutes((int)Math.Round(delta.Y / _minutePixels));
        switch (_drag)
        {
            case DragMode.Move:
                _dragMission!.StartMinutes = Math.Max(0, _pressStart + deltaMinutes);
                break;
            case DragMode.Resize:
                _dragMission!.DurationMinutes = Math.Max(Snap, _pressDuration + deltaMinutes);
                break;
            case DragMode.Pan:
                _scrollY = _pressStart - delta.Y;
                _scrollX = _pressDuration - delta.X;
                UpdateScale(range);
                break;
        }

        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var columns = ColumnItems;
        if (_drag == DragMode.Move && _dragMoved && _dragMission is { } mission)
        {
            // Déposée dans une autre colonne : la mission passe à cette équipe.
            var target = ColumnAt(e.GetPosition(this).X, columns.Count, ColumnWidth(columns.Count));
            if (target >= 0 && target != _pressColumn && _pressColumn < columns.Count)
                mission.ReplaceTeam(columns[_pressColumn].Model.Id, columns[target].Model.Id);
        }
        else if (_drag == DragMode.Pan && !_dragMoved)
        {
            SelectedMission = null;
        }

        _drag = DragMode.None;
        _dragMission = null;
        e.Pointer.Capture(null);
        InvalidateVisual();
    }

    private (MissionViewModel Mission, int Column, bool OnResizeHandle)? HitTest(Point position, IReadOnlyList<TeamViewModel> columns, double width, int rangeStart)
    {
        var column = ColumnAt(position.X, columns.Count, width);
        if (column < 0 || position.Y < HeaderHeight)
            return null;

        var teamId = columns[column].Model.Id;
        var lanes = ComputeLanes(columns);
        // La mission sélectionnée est dessinée au-dessus : elle est prioritaire.
        var candidates = MissionItems.Where(m => m.TeamIds.Contains(teamId)).OrderBy(m => m == SelectedMission).Reverse();
        foreach (var mission in candidates)
        {
            var rect = BlockRect(mission, column, width, rangeStart, lanes);
            if (rect.Contains(position))
                return (mission, column, position.Y >= rect.Bottom - ResizeHandle);
        }

        return null;
    }

    private static int FirstColumn(MissionViewModel mission, IReadOnlyList<TeamViewModel> columns)
    {
        for (var i = 0; i < columns.Count; i++)
        {
            if (mission.TeamIds.Contains(columns[i].Model.Id))
                return i;
        }

        return -1;
    }

    private static int SnapMinutes(int minutes) => (int)Math.Round(minutes / (double)Snap) * Snap;

    private static bool IsLight(Color color) => 0.299 * color.R + 0.587 * color.G + 0.114 * color.B > 160;

    private static Color Darken(Color color) => Color.FromRgb((byte)(color.R * 0.6), (byte)(color.G * 0.6), (byte)(color.B * 0.6));

    // ----- Suivi des données -----

    private void Observe()
    {
        foreach (var item in _observed)
            item.PropertyChanged -= OnItemChanged;
        foreach (var collection in _observedCollections)
            collection.CollectionChanged -= OnCollectionChanged;
        _observed.Clear();
        _observedCollections.Clear();

        foreach (var collection in new[] { Columns, Missions }.OfType<INotifyCollectionChanged>())
        {
            collection.CollectionChanged += OnCollectionChanged;
            _observedCollections.Add(collection);
        }

        IEnumerable<INotifyPropertyChanged> items = MissionItems.Cast<INotifyPropertyChanged>()
            .Concat(ColumnItems)
            .Concat(ColumnItems.Select(t => t.Faction).OfType<INotifyPropertyChanged>().Distinct());
        foreach (var item in items)
        {
            item.PropertyChanged += OnItemChanged;
            _observed.Add(item);
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Observe();
        InvalidateVisual();
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is TeamViewModel && e.PropertyName == nameof(TeamViewModel.Faction))
            Observe();
        InvalidateVisual();
    }
}
