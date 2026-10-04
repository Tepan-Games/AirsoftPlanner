namespace AirsoftPlanner.Core.Domain;

public enum ZoneKind
{
    /// <summary>Surface délimitée par un polygone (base, secteur, zone interdite...).</summary>
    Area,

    /// <summary>Point précis (objectif, caisse, point de respawn...).</summary>
    Point,
}

/// <summary>Une zone du terrain où se déroulent des missions.</summary>
public class Zone : Entity
{
    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    public ZoneKind Kind { get; set; }

    public string Color { get; set; } = "#F9A825";

    /// <summary>Sommets du polygone (Area) ou position unique (Point).</summary>
    public List<GeoPoint> Points { get; set; } = [];

    /// <summary>Nature : bivouac, campement, respawn, objectif...</summary>
    public PoiCategory Category { get; set; }

    /// <summary>Orga seulement, toutes les équipes ou une faction (voir <see cref="VisibleFactionId"/>).</summary>
    public ZoneVisibility Visibility { get; set; }

    public Guid? VisibleFactionId { get; set; }

    /// <summary>Symbole militaire du point (Auto : selon la catégorie).</summary>
    public AirsoftPlanner.Core.Symbols.MilSymbol Symbol { get; set; }

    public AirsoftPlanner.Core.Symbols.Echelon Echelon { get; set; }

    /// <summary>Faction à qui appartient le point (couleur du symbole) ; null : couleur du point.</summary>
    public Guid? OwnerFactionId { get; set; }
}
