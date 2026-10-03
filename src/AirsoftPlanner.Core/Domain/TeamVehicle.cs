namespace AirsoftPlanner.Core.Domain;

/// <summary>Véhicules amenés par une équipe (une ligne par type).</summary>
public class TeamVehicle : Entity
{
    public Guid TeamId { get; set; }

    /// <summary>Type de véhicule (4x4, quad, pick-up, camion...).</summary>
    public string Kind { get; set; } = "";

    public int Quantity { get; set; } = 1;

    public string Notes { get; set; } = "";

    public int SortOrder { get; set; }

    /// <summary>Véhicule utilisé dans le jeu : son carburant est remboursé à l'équipe.</summary>
    public bool InGame { get; set; }

    /// <summary>Identifiant du traceur GPS du véhicule (Traccar Client sur un téléphone à bord, boîtier...).</summary>
    public string GpsDeviceId { get; set; } = "";

    /// <summary>Relevé du compteur au départ de l'OP (km).</summary>
    public decimal? OdometerStartKm { get; set; }

    /// <summary>Relevé du compteur à la fin de l'OP (km).</summary>
    public decimal? OdometerEndKm { get; set; }
}
