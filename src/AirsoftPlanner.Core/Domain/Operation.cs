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

    /// <summary>Participation aux frais demandée par joueur.</summary>
    public decimal PricePerPlayer { get; set; }

    /// <summary>Fréquence radio de l'orga (PC de l'OP, arbitres).</summary>
    public string OrgaRadioFrequency { get; set; } = "";

    /// <summary>Numéro de téléphone d'urgence de l'orga (blessure, incident).</summary>
    public string EmergencyPhone { get; set; } = "";

    /// <summary>Carburant remboursé par kilomètre aux véhicules mis en jeu.</summary>
    public decimal FuelRatePerKm { get; set; }

    /// <summary>Intervalle d'envoi des positions par l'application Android (secondes).</summary>
    public int TrackingIntervalSeconds { get; set; } = 30;

    /// <summary>Ce que l'application Android montre des équipes alliées.</summary>
    public AirsoftPlanner.Core.Gps.AllyShareMode AllyShareMode { get; set; } = AirsoftPlanner.Core.Gps.AllyShareMode.Coordinates;
}
