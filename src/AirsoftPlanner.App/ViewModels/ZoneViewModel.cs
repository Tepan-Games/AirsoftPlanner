using System;
using System.Collections.Generic;
using System.Linq;
using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Core.Geo;

namespace AirsoftPlanner.App.ViewModels;

public record PoiCategoryOption(PoiCategory Value)
{
    public static IReadOnlyList<PoiCategoryOption> All { get; } = PoiCategories.All.Select(c => new PoiCategoryOption(c)).ToList();

    public string Label => $"{PoiCategories.Symbol(Value)} {PoiCategories.Label(Value)}".Trim();

    public override string ToString() => Label;
}

/// <summary>Qui voit la zone : l'orga seulement, toutes les équipes ou une faction.</summary>
public record ZoneVisibilityOption(ZoneVisibility Value, Guid? FactionId, string Label)
{
    public override string ToString() => Label;
}

public class ZoneViewModel(Zone zone, Func<CoordinateFormat> coordinateFormat) : ViewModelBase
{
    public Zone Model => zone;

    public string Name
    {
        get => zone.Name;
        set
        {
            if (SetProperty(zone.Name, value, zone, (z, v) => z.Name = v))
                OnPropertyChanged(nameof(DisplayName));
        }
    }

    public string Description
    {
        get => zone.Description;
        set => SetProperty(zone.Description, value, zone, (z, v) => z.Description = v);
    }

    public PoiCategoryOption Category
    {
        get => PoiCategoryOption.All.First(o => o.Value == zone.Category);
        set
        {
            if (value is null || value.Value == zone.Category)
                return;
            zone.Category = value.Value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayName));
        }
    }

    /// <summary>Nom précédé du symbole de la catégorie (⛺ Bivouac nord).</summary>
    public string DisplayName => $"{PoiCategories.Symbol(zone.Category)} {zone.Name}".Trim();

    public string Color
    {
        get => zone.Color;
        set => SetProperty(zone.Color, value, zone, (z, v) => z.Color = v);
    }

    public ZoneKind Kind => zone.Kind;

    public bool IsArea => zone.Kind == ZoneKind.Area;

    public string KindLabel => IsArea ? "Zone" : "Point";

    public IReadOnlyList<GeoPoint> Points => zone.Points;

    /// <summary>Une zone a besoin de 3 sommets, un point d'une position.</summary>
    public bool IsComplete => zone.Points.Count >= (IsArea ? 3 : 1);

    public string Summary => zone.Points.Count switch
    {
        0 => "Pas encore placé sur la carte",
        _ when !IsArea => Coordinates.Format(zone.Points[0], coordinateFormat()),
        < 3 => $"{zone.Points.Count} sommet(s) : il en faut au moins 3",
        _ => $"{zone.Points.Count} sommets",
    };

    /// <summary>Position d'un point, éditable dans n'importe quel format de coordonnées.</summary>
    public string PositionText
    {
        get => zone.Points.Count == 0 ? "" : Coordinates.Format(zone.Points[0], coordinateFormat());
        set
        {
            if (!Coordinates.TryParse(value, out var point))
                throw new FormatException("Coordonnées non reconnues (ex. 31T 448251 5411952 ou 48.8583, 2.2944).");

            SetPoints([point]);
        }
    }

    /// <summary>Ajoute un sommet (zone) ou place le point.</summary>
    public void AddPoint(GeoPoint point)
    {
        if (IsArea)
            SetPoints([.. zone.Points, point]);
        else
            SetPoints([point]);
    }

    public void MovePoint(int index, GeoPoint point)
    {
        var points = new List<GeoPoint>(zone.Points) { [index] = point };
        SetPoints(points);
    }

    public void RemoveLastPoint()
    {
        if (zone.Points.Count > 0)
            SetPoints(zone.Points.GetRange(0, zone.Points.Count - 1));
    }

    public void ClearPoints() => SetPoints([]);

    /// <summary>À appeler quand le format des coordonnées de l'OP change.</summary>
    public void RefreshCoordinates()
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(PositionText));
    }

    private void SetPoints(List<GeoPoint> points)
    {
        // Nouvelle liste à chaque modification : EF Core détecte le changement de façon fiable.
        zone.Points = points;
        OnPropertyChanged(nameof(Points));
        OnPropertyChanged(nameof(IsComplete));
        RefreshCoordinates();
    }
}
