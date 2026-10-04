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

namespace AirsoftPlanner.App.ViewModels;

/// <summary>Terrain : emprise, fonds de carte (IGN ou image) et zones.</summary>
public partial class TerrainViewModel : ViewModelBase
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

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
                return "Emprise non définie : saisissez le centre du terrain ou ses deux coins.";
            var (width, height) = Area.SizeInMeters();
            return string.Format(French, "Emprise : {0:0.00} × {1:0.00} km", width / 1000, height / 1000);
        }
    }

    public string DownloadSummary
    {
        get
        {
            if (!HasArea)
                return "";
            var plan = TileGrid.BestPlan(Area, SelectedSource.MaxZoom, MapDownloader.MaxImageSide);
            return string.Format(French, "{0} tuiles · zoom {1} · {2:0.0} m par pixel", plan.TileCount, plan.Zoom, plan.MetersPerPixel);
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
            OnPropertyChanged();
        }
    }

    private void RefreshVisibilityOptions()
    {
        VisibilityOptions.Clear();
        VisibilityOptions.Add(new ZoneVisibilityOption(ZoneVisibility.Orga, null, "Orga seulement"));
        VisibilityOptions.Add(new ZoneVisibilityOption(ZoneVisibility.AllTeams, null, "Toutes les équipes"));
        foreach (var faction in _factions?.Items ?? [])
            VisibilityOptions.Add(new ZoneVisibilityOption(ZoneVisibility.Faction, faction.Model.Id, $"Faction {faction.Name}"));
        OnPropertyChanged(nameof(SelectedZoneVisibility));
        OwnerOptions.Clear();
        OwnerOptions.Add(new FactionChoice(null, "Aucune (couleur du point)"));
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
            await _dialogs.ShowErrorAsync("Coordonnées du centre non reconnues (ex. 31T 448251 5411952, 48.8583, 2.2944 ou 48°51'30\"N 2°17'40\"E).");
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
            await _dialogs.ShowErrorAsync("Définissez d'abord l'emprise du terrain (centre et taille, ou deux coins).");
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
            await _dialogs.ShowErrorAsync($"Téléchargement impossible : vérifiez la connexion Internet.\n({ex.Message})");
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
            await _dialogs.ShowErrorAsync("Définissez d'abord l'emprise du terrain : l'image importée doit couvrir exactement cette emprise (nord en haut).");
            return;
        }

        var path = await _dialogs.PickImageFileAsync();
        if (path is null)
            return;

        AddLayer(new MapLayer
        {
            Name = Path.GetFileNameWithoutExtension(path),
            Attribution = "Image importée",
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
    private void AddArea() => AddZone(ZoneKind.Area, "Zone");

    [RelayCommand]
    private void AddPoint() => AddZone(ZoneKind.Point, "Point");

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
                throw new FormatException("Les deux coins doivent être différents.");
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
        : throw new FormatException("Coordonnées non reconnues (ex. 31T 448251 5411952 ou 48.8583, 2.2944).");
}
