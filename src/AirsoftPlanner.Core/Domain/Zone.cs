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
}
