using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AirsoftPlanner.App.Services;
using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Core.Geo;
using AirsoftPlanner.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AirsoftPlanner.Core.Localization;

namespace AirsoftPlanner.App.ViewModels;

/// <summary>Terrain : emprise, fonds de carte (IGN ou image) et zones.</summary>
public partial class TerrainViewModel : ViewModelBase
{
    private static CultureInfo French => AirsoftPlanner.Core.Localization.L.Culture;

    private readonly OperationFile _file;
    private readonly OperationViewModel _operation;
    private readonly IFileDialogService _dialogs;
    private readonly MapDownloader _downloader = new();
    private CancellationTokenSource? _downloadCancellation;

    public TerrainViewModel(OperationFile file, OperationViewModel operation, IFileDialogService dialogs)
    {
        _file = file;
        _operation = operation;
        _dialogs = dialogs;
        Layers = new ObservableCollection<MapLayerViewModel>(file.LoadMapLayers().Select(l => new MapLayerViewModel(l)));
        Zones = new ObservableCollection<ZoneViewModel>(file.LoadZones().Select(z => new ZoneViewModel(z, () => operation.CoordinateFormat, FactionColor)));
        _selectedLayer = Layers.FirstOrDefault();
        _selectedSource = MapSource.All[0];
        _areaSizeKm = 1.5m;
        operation.PropertyChanged += OnOperationChanged;
    }

    // ----- Fonds de carte -----

    public ObservableCollection<MapLayerViewModel> Layers { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ViewBounds), nameof(Attribution))]
    [NotifyCanExecuteChangedFor(nameof(RemoveLayerCommand))]
    private MapLayerViewModel? _selectedLayer;

    public string Attribution => SelectedLayer?.Attribution ?? "";

    public IReadOnlyList<MapSource> Sources => MapSource.All;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DownloadSummary))]
    private MapSource _selectedSource;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand), nameof(ImportImageCommand))]
    private bool _isDownloading;

    [ObservableProperty]
    private double _downloadProgress;

    // ----- Emprise du terrain -----

    public GeoBounds Area => _file.TerrainMap.Bounds;

    public bool HasArea => Area.IsValid;

    public string NorthWestText
    {
        get => HasArea ? FormatPoint(new GeoPoint(Area.North, Area.West)) : "";
        set => SetAreaCorners(Parse(value), HasArea ? new GeoPoint(Area.South, Area.East) : null);
    }

    public string SouthEastText
    {
        get => HasArea ? FormatPoint(new GeoPoint(Area.South, Area.East)) : "";
        set => SetAreaCorners(HasArea ? new GeoPoint(Area.North, Area.West) : null, Parse(value));
    }

    [ObservableProperty]
    private string _centerText = "";

    [ObservableProperty]
    private decimal? _areaSizeKm;

    public string AreaSummary
    {
        get
        {
            if (!HasArea)
                return L.T("emprise_non_definie_saisissez_le_centre_du_terra");
            var (width, height) = Area.SizeInMeters();
            return string.Format(French, L.T("emprise_x_x_km"), width / 1000, height / 1000);
        }
    }

    public string DownloadSummary
    {
        get
        {
            if (!HasArea)
                return "";
            var plan = TileGrid.BestPlan(Area, SelectedSource.MaxZoom, MapDownloader.MaxImageSide);
            return string.Format(French, L.T("x_tuiles_zoom_x_x_m_par_pixel"), plan.TileCount, plan.Zoom, plan.MetersPerPixel);
        }
    }

    // ----- Zones -----

    public ObservableCollection<ZoneViewModel> Zones { get; }

    /// <summary>Déclenché quand une zone est supprimée, pour la détacher des missions.</summary>
    public event Action<ZoneViewModel>? ZoneRemoved;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedZone))]
    [NotifyCanExecuteChangedFor(nameof(RemoveZoneCommand), nameof(UndoPointCommand), nameof(ClearPointsCommand))]
    private ZoneViewModel? _selectedZone;

    public bool HasSelectedZone => SelectedZone is not null;

    public IReadOnlyList<PoiCategoryOption> Categories => PoiCategoryOption.All;

    public IReadOnlyList<MilSymbolOption> Symbols => MilSymbolOption.All;

    public IReadOnlyList<EchelonOption> Echelons => EchelonOption.All;

    /// <summary>Choix « Faction (couleur du symbole) ».</summary>
    public ObservableCollection<FactionChoice> OwnerOptions { get; } = [];

    public FactionChoice? SelectedZoneOwner
    {
        get => SelectedZone?.Model is { } zone ? OwnerOptions.FirstOrDefault(o => o.Id == zone.OwnerFactionId) : null;
        set
        {
            if (value is null || SelectedZone?.Model is not { } zone)
                return;
            zone.OwnerFactionId = value.Id;
            SelectedZone.NotifySymbolColorChanged();
            OnPropertyChanged();
        }
    }

    private MissionsViewModel? _missions;

    /// <summary>Missions de l'OP (points diffusés seulement pendant une mission).</summary>
    public void AttachMissions(MissionsViewModel missions)
    {
        _missions = missions;
        OnPropertyChanged(nameof(Missions));
    }

    public IEnumerable<MissionViewModel> Missions => _missions?.Missions.OrderBy(m => m.StartMinutes) ?? Enumerable.Empty<MissionViewModel>();

    /// <summary>Visibilité « pendant une mission » : choix de la mission.</summary>
    public bool IsMissionVisibility => SelectedZone?.Model.Visibility == ZoneVisibility.DuringMission;

    public MissionViewModel? SelectedZoneMission
    {
        get => SelectedZone?.Model.VisibleMissionId is { } id ? _missions?.Missions.FirstOrDefault(m => m.Model.Id == id) : null;
        set
        {
            if (SelectedZone?.Model is not { } zone)
                return;
            zone.VisibleMissionId = value?.Model.Id;
            OnPropertyChanged();
            foreach (var mission in _missions?.Missions ?? [])
                mission.RefreshPoints();
        }
    }

    private string? FactionColor(Guid id) => _factions?.Items.FirstOrDefault(f => f.Model.Id == id)?.Color;

    private FactionsViewModel? _factions;

    /// <summary>Choix « Visible par » : orga seulement, toutes les équipes, ou une faction.</summary>
    public ObservableCollection<ZoneVisibilityOption> VisibilityOptions { get; } = [];

    /// <summary>Factions de l'OP, pour la visibilité des zones (téléphones, ordres de mission).</summary>
    public void AttachFactions(FactionsViewModel factions)
    {
        _factions = factions;
        factions.Items.CollectionChanged += (_, _) => RefreshVisibilityOptions();
        foreach (var faction in factions.Items)
            faction.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(FactionViewModel.Name))
                    RefreshVisibilityOptions();
                if (e.PropertyName == nameof(FactionViewModel.Color))
                    foreach (var zone in Zones)
                        zone.NotifySymbolColorChanged();
            };
        RefreshVisibilityOptions();
    }

    public ZoneVisibilityOption? SelectedZoneVisibility
    {
        get => SelectedZone?.Model is { } zone
            ? VisibilityOptions.FirstOrDefault(o => o.Value == zone.Visibility && (o.Value != ZoneVisibility.Faction || o.FactionId == zone.VisibleFactionId))
            : null;
        set
        {
            if (value is null || SelectedZone?.Model is not { } zone)
                return;
            (zone.Visibility, zone.VisibleFactionId) = (value.Value, value.Value == ZoneVisibility.Faction ? value.FactionId : null);
            if (value.Value != ZoneVisibility.DuringMission)
                zone.VisibleMissionId = null;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsMissionVisibility));
            OnPropertyChanged(nameof(SelectedZoneMission));
        }
    }

    private void RefreshVisibilityOptions()
    {
        VisibilityOptions.Clear();
        VisibilityOptions.Add(new ZoneVisibilityOption(ZoneVisibility.Orga, null, L.T("orga_seulement")));
        VisibilityOptions.Add(new ZoneVisibilityOption(ZoneVisibility.AllTeams, null, L.T("toutes_les_equipes")));
        VisibilityOptions.Add(new ZoneVisibilityOption(ZoneVisibility.DuringMission, null, L.T("pendant_une_mission")));
        foreach (var faction in _factions?.Items ?? [])
            VisibilityOptions.Add(new ZoneVisibilityOption(ZoneVisibility.Faction, faction.Model.Id, L.F("faction_x", faction.Name)));
        OnPropertyChanged(nameof(SelectedZoneVisibility));
        OwnerOptions.Clear();
        OwnerOptions.Add(new FactionChoice(null, L.T("aucune_couleur_du_point")));
        foreach (var faction in _factions?.Items ?? [])
            OwnerOptions.Add(new FactionChoice(faction.Model.Id, faction.Name));
        OnPropertyChanged(nameof(SelectedZoneOwner));
    }

    /// <summary>En mode tracé, chaque clic sur la carte ajoute un sommet à la zone sélectionnée.</summary>
    [ObservableProperty]
    private bool _isDrawing;

    [ObservableProperty]
    private bool _showUtmGrid = true;

    // ----- Carte -----

    /// <summary>Rectangle cadré par la carte : le fond sélectionné, sinon l'emprise, sinon les zones.</summary>
    public GeoBounds? ViewBounds
    {
        get
        {
            if (SelectedLayer is not null)
                return SelectedLayer.Bounds;
            if (HasArea)
                return Area;
            var points = Zones.SelectMany(z => z.Points).ToList();
            return points.Count > 0 ? GeoBounds.Around(points) : null;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PointerText))]
    private GeoPoint? _pointerPosition;

    public string PointerText => PointerPosition is { } point ? FormatPoint(point) : "";

    // ----- Commandes : emprise et fonds -----

    [RelayCommand]
    private async Task ApplyCenterAsync()
    {
        if (!Coordinates.TryParse(CenterText, out var center))
        {
            await _dialogs.ShowErrorAsync(L.T("coordonnees_du_centre_non_reconnues_ex_31t_44825"));
            return;
        }

        var size = (double)Math.Clamp(AreaSizeKm ?? 1.5m, 0.1m, 20m) * 1000;
        var corner = UtmCoordinate.FromGeo(center);
        var northWest = corner with { Easting = corner.Easting - size / 2, Northing = corner.Northing + size / 2 };
        var southEast = corner with { Easting = corner.Easting + size / 2, Northing = corner.Northing - size / 2 };
        SetAreaCorners(northWest.ToGeo(), southEast.ToGeo());
    }

    [RelayCommand(CanExecute = nameof(CanStartDownload))]
    private async Task DownloadAsync()
    {
        if (!HasArea)
        {
            await _dialogs.ShowErrorAsync(L.T("definissez_d_abord_l_emprise_du_terrain_centre_e"));
            return;
        }

        var plan = TileGrid.BestPlan(Area, SelectedSource.MaxZoom, MapDownloader.MaxImageSide);
        _downloadCancellation = new CancellationTokenSource();
        IsDownloading = true;
        DownloadProgress = 0;
        try
        {
            var progress = new Progress<double>(value => DownloadProgress = value * 100);
            var layer = await Task.Run(() => _downloader.DownloadAsync(SelectedSource, plan, progress, _downloadCancellation.Token));
            AddLayer(layer);
        }
        catch (OperationCanceledException)
        {
        }
        catch (HttpRequestException ex)
        {
            await _dialogs.ShowErrorAsync(L.F("telechargement_impossible_verifiez_la_connexion", ex.Message));
        }
        finally
        {
            IsDownloading = false;
            _downloadCancellation.Dispose();
            _downloadCancellation = null;
        }
    }

    [RelayCommand]
    private void CancelDownload() => _downloadCancellation?.Cancel();

    [RelayCommand(CanExecute = nameof(CanStartDownload))]
    private async Task ImportImageAsync()
    {
        if (!HasArea)
        {
            await _dialogs.ShowErrorAsync(L.T("definissez_d_abord_l_emprise_du_terrain_l_image"));
            return;
        }

        var path = await _dialogs.PickImageFileAsync();
        if (path is null)
            return;

        AddLayer(new MapLayer
        {
            Name = Path.GetFileNameWithoutExtension(path),
            Attribution = L.T("image_importee"),
            Image = await File.ReadAllBytesAsync(path),
            Bounds = Area,
        });
    }

    [RelayCommand(CanExecute = nameof(HasSelectedLayer))]
    private void RemoveLayer()
    {
        var layer = SelectedLayer!;
        _file.Remove(layer.Model);
        Layers.Remove(layer);
        SelectedLayer = Layers.FirstOrDefault();
    }

    // ----- Commandes : zones -----

    [RelayCommand]
    private void AddArea() => AddZone(ZoneKind.Area, L.T("zone"));

    [RelayCommand]
    private void AddPoint() => AddZone(ZoneKind.Point, L.T("point_2"));

    [RelayCommand(CanExecute = nameof(HasSelectedZone))]
    private void RemoveZone()
    {
        var zone = SelectedZone!;
        _file.Remove(zone.Model);
        Zones.Remove(zone);
        ZoneRemoved?.Invoke(zone);
        SelectedZone = Zones.FirstOrDefault();
        IsDrawing = false;
    }

    [RelayCommand(CanExecute = nameof(HasSelectedZone))]
    private void UndoPoint() => SelectedZone!.RemoveLastPoint();

    [RelayCommand(CanExecute = nameof(HasSelectedZone))]
    private void ClearPoints() => SelectedZone!.ClearPoints();

    /// <summary>Clic sur la carte en mode tracé.</summary>
    [RelayCommand]
    private void MapClicked(GeoPoint point)
    {
        if (!IsDrawing || SelectedZone is null)
            return;

        SelectedZone.AddPoint(point);
        if (!SelectedZone.IsArea)
            IsDrawing = false;
    }

    partial void OnSelectedZoneChanged(ZoneViewModel? value)
    {
        OnPropertyChanged(nameof(SelectedZoneVisibility));
        OnPropertyChanged(nameof(SelectedZoneOwner));
        OnPropertyChanged(nameof(IsMissionVisibility));
        OnPropertyChanged(nameof(SelectedZoneMission));
        if (value is null || value.IsComplete)
            IsDrawing = false;
    }

    private bool HasSelectedLayer => SelectedLayer is not null;

    private bool CanStartDownload => !IsDownloading;

    /// <summary>Ajoute un point déjà placé (ex. lieu d'une urgence signalée pendant l'OP).</summary>
    public ZoneViewModel AddPointZone(string name, GeoPoint point, string color)
    {
        var zone = new Zone { Name = name, Kind = ZoneKind.Point, Color = color, Points = [point] };
        _file.Add(zone);
        var viewModel = new ZoneViewModel(zone, () => _operation.CoordinateFormat, FactionColor);
        Zones.Add(viewModel);
        return viewModel;
    }

    private void AddZone(ZoneKind kind, string prefix)
    {
        var zone = new Zone
        {
            Name = $"{prefix} {Zones.Count(z => z.Kind == kind) + 1}",
            Kind = kind,
            Color = kind == ZoneKind.Area ? "#F9A825" : "#C62828",
        };
        _file.Add(zone);
        var viewModel = new ZoneViewModel(zone, () => _operation.CoordinateFormat, FactionColor);
        Zones.Add(viewModel);
        SelectedZone = viewModel;
        IsDrawing = true;
    }

    private void AddLayer(MapLayer layer)
    {
        layer.SortOrder = Layers.Count == 0 ? 0 : Layers.Max(l => l.Model.SortOrder) + 1;
        _file.Add(layer);
        var viewModel = new MapLayerViewModel(layer);
        Layers.Add(viewModel);
        SelectedLayer = viewModel;
    }

    private void SetAreaCorners(GeoPoint? northWest, GeoPoint? southEast)
    {
        if (northWest is { } nw && southEast is { } se)
        {
            var bounds = new GeoBounds(
                Math.Max(nw.Latitude, se.Latitude), Math.Min(nw.Latitude, se.Latitude),
                Math.Min(nw.Longitude, se.Longitude), Math.Max(nw.Longitude, se.Longitude));
            if (!bounds.IsValid)
                throw new FormatException(L.T("les_deux_coins_doivent_etre_differents"));
            _file.TerrainMap.Bounds = bounds;
        }
        else if (northWest is { } only1)
        {
            _file.TerrainMap.Bounds = GeoBounds.Around([only1]);
        }
        else if (southEast is { } only2)
        {
            _file.TerrainMap.Bounds = GeoBounds.Around([only2]);
        }

        RefreshArea();
    }

    private void RefreshArea()
    {
        OnPropertyChanged(nameof(Area));
        OnPropertyChanged(nameof(HasArea));
        OnPropertyChanged(nameof(NorthWestText));
        OnPropertyChanged(nameof(SouthEastText));
        OnPropertyChanged(nameof(AreaSummary));
        OnPropertyChanged(nameof(DownloadSummary));
        OnPropertyChanged(nameof(ViewBounds));
    }

    private void OnOperationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(OperationViewModel.CoordinateFormat))
            return;

        OnPropertyChanged(nameof(NorthWestText));
        OnPropertyChanged(nameof(SouthEastText));
        OnPropertyChanged(nameof(PointerText));
        foreach (var zone in Zones)
            zone.RefreshCoordinates();
    }

    private string FormatPoint(GeoPoint point) => Coordinates.Format(point, _operation.CoordinateFormat);

    private static GeoPoint Parse(string text) => Coordinates.TryParse(text, out var point)
        ? point
        : throw new FormatException(L.T("coordonnees_non_reconnues_ex_31t_448251_5411952"));
}
