using System;
using System.Collections.Generic;
using System.Linq;
using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Core.Geo;
using AirsoftPlanner.Core.Localization;

namespace AirsoftPlanner.App.ViewModels;

public record PoiCategoryOption(PoiCategory Value)
{
    public static IReadOnlyList<PoiCategoryOption> All { get; } = PoiCategories.All.Select(c => new PoiCategoryOption(c)).ToList();

    public string Label => $"{PoiCategories.Symbol(Value)} {PoiCategories.Label(Value)}".Trim();

    public override string ToString() => Label;
}

public record MilSymbolOption(AirsoftPlanner.Core.Symbols.MilSymbol Value)
{
    public static IReadOnlyList<MilSymbolOption> All { get; } = AirsoftPlanner.Core.Symbols.MilitarySymbols.All.Select(s => new MilSymbolOption(s)).ToList();

    /// <summary>Symboles proposés pour une équipe (pas de point tactique).</summary>
    public static IReadOnlyList<MilSymbolOption> ForTeams { get; } = All.Where(o => o.Value is not (AirsoftPlanner.Core.Symbols.MilSymbol.Dot
        or AirsoftPlanner.Core.Symbols.MilSymbol.Objective or AirsoftPlanner.Core.Symbols.MilSymbol.RallyPoint or AirsoftPlanner.Core.Symbols.MilSymbol.Checkpoint
        or AirsoftPlanner.Core.Symbols.MilSymbol.Danger or AirsoftPlanner.Core.Symbols.MilSymbol.LandingZone or AirsoftPlanner.Core.Symbols.MilSymbol.Installation
        or AirsoftPlanner.Core.Symbols.MilSymbol.Bivouac)).ToList();

    public string Label => AirsoftPlanner.Core.Symbols.MilitarySymbols.Label(Value);

    /// <summary>Aperçu : symbole « Auto » montré comme infanterie.</summary>
    public AirsoftPlanner.Core.Symbols.MilSymbol Preview => Value == AirsoftPlanner.Core.Symbols.MilSymbol.Auto ? AirsoftPlanner.Core.Symbols.MilSymbol.Infantry : Value;

    public override string ToString() => Label;
}

public record EchelonOption(AirsoftPlanner.Core.Symbols.Echelon Value)
{
    public static IReadOnlyList<EchelonOption> All { get; } = Enum.GetValues<AirsoftPlanner.Core.Symbols.Echelon>().Select(e => new EchelonOption(e)).ToList();

    public string Label => AirsoftPlanner.Core.Symbols.MilitarySymbols.Label(Value);

    public override string ToString() => Label;
}

/// <summary>Faction propriétaire d'un point (couleur de son symbole).</summary>
public record FactionChoice(Guid? Id, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Qui voit la zone : l'orga seulement, toutes les équipes ou une faction.</summary>
public record ZoneVisibilityOption(ZoneVisibility Value, Guid? FactionId, string Label)
{
    public override string ToString() => Label;
}

public class ZoneViewModel(Zone zone, Func<CoordinateFormat> coordinateFormat, Func<Guid, string?>? factionColor = null) : ViewModelBase
{
    public MilSymbolOption Symbol
    {
        get => MilSymbolOption.All.First(o => o.Value == zone.Symbol);
        set
        {
            if (value is null || value.Value == zone.Symbol)
                return;
            zone.Symbol = value.Value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ResolvedSymbol));
        }
    }

    public EchelonOption Echelon
    {
        get => EchelonOption.All.First(o => o.Value == zone.Echelon);
        set
        {
            if (value is null || value.Value == zone.Echelon)
                return;
            zone.Echelon = value.Value;
            OnPropertyChanged();
        }
    }

    /// <summary>Symbole dessiné (Auto remplacé par celui de la catégorie).</summary>
    public AirsoftPlanner.Core.Symbols.MilSymbol ResolvedSymbol => AirsoftPlanner.Core.Symbols.MilitarySymbols.Resolve(zone.Symbol, zone.Category);

    /// <summary>Couleur du symbole : celle de la faction propriétaire, sinon celle du point.</summary>
    public string SymbolColor => zone.OwnerFactionId is { } id && factionColor?.Invoke(id) is { } color ? color : zone.Color;

    public void NotifySymbolColorChanged() => OnPropertyChanged(nameof(SymbolColor));

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
            OnPropertyChanged(nameof(ResolvedSymbol));
        }
    }

    /// <summary>Nom précédé du symbole de la catégorie (⛺ Bivouac nord).</summary>
    public string DisplayName => $"{PoiCategories.Symbol(zone.Category)} {zone.Name}".Trim();

    public string Color
    {
        get => zone.Color;
        set
        {
            if (SetProperty(zone.Color, value, zone, (z, v) => z.Color = v))
                OnPropertyChanged(nameof(SymbolColor));
        }
    }

    public ZoneKind Kind => zone.Kind;

    public bool IsArea => zone.Kind == ZoneKind.Area;

    public string KindLabel => IsArea ? L.T("zone") : L.T("point_2");

    public IReadOnlyList<GeoPoint> Points => zone.Points;

    /// <summary>Une zone a besoin de 3 sommets, un point d'une position.</summary>
    public bool IsComplete => zone.Points.Count >= (IsArea ? 3 : 1);

    public string Summary => zone.Points.Count switch
    {
        0 => L.T("pas_encore_place_sur_la_carte"),
        _ when !IsArea => Coordinates.Format(zone.Points[0], coordinateFormat()),
        < 3 => L.F("x_sommet_s_il_en_faut_au_moins_3", zone.Points.Count),
        _ => $"{zone.Points.Count} sommets",
    };

    /// <summary>Position d'un point, éditable dans n'importe quel format de coordonnées.</summary>
    public string PositionText
    {
        get => zone.Points.Count == 0 ? "" : Coordinates.Format(zone.Points[0], coordinateFormat());
        set
        {
            if (!Coordinates.TryParse(value, out var point))
                throw new FormatException(L.T("coordonnees_non_reconnues_ex_31t_448251_5411952"));

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
