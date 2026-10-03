using AirsoftPlanner.Core.Geo;

namespace AirsoftPlanner.Core.Domain;

/// <summary>Une OP (partie organisée). Un fichier d'OP contient exactement une opération.</summary>
public class Operation : Entity
{
    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    public string Location { get; set; } = "";

    public DateTimeOffset StartsAt { get; set; }

    public DateTimeOffset EndsAt { get; set; }

    /// <summary>Format des coordonnées affichées dans l'OP et ses documents.</summary>
    public CoordinateFormat CoordinateFormat { get; set; } = CoordinateFormat.Utm;

    /// <summary>Vitesse de déplacement à pied retenue pour estimer les retards (terrain, équipement, prudence).</summary>
    public double WalkingSpeedKmh { get; set; } = 3;
}
