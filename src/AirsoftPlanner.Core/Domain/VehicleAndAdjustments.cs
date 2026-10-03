namespace AirsoftPlanner.Core.Domain;

/// <summary>Position reçue du traceur GPS d'un véhicule mis en jeu (pour calculer ses kilomètres).</summary>
public class VehiclePosition : Entity
{
    public Guid VehicleId { get; set; }

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public DateTimeOffset At { get; set; }

    public GeoPoint Point
    {
        get => new(Latitude, Longitude);
        set => (Latitude, Longitude) = (value.Latitude, value.Longitude);
    }
}

public enum AdjustmentKind
{
    /// <summary>Remise sur la participation (ex. équipe qui aide à l'organisation).</summary>
    Discount,

    /// <summary>Cadeau ou geste commercial (entrée offerte...).</summary>
    Gift,
}

/// <summary>Remise ou cadeau accordé à une équipe, déduit de la somme qu'elle doit.</summary>
public class TeamAdjustment : Entity
{
    public Guid TeamId { get; set; }

    public AdjustmentKind Kind { get; set; }

    public string Label { get; set; } = "";

    /// <summary>Montant déduit (0 pour un cadeau sans valeur monétaire, simplement noté).</summary>
    public decimal Amount { get; set; }
}
