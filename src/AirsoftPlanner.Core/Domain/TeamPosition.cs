namespace AirsoftPlanner.Core.Domain;

/// <summary>Position d'une équipe reçue à un instant donné (saisie manuelle, GPS...).</summary>
public class TeamPosition : Entity
{
    public Guid TeamId { get; set; }

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }

    /// <summary>Origine de la position (« Manuel », « Smartphone », « Meshtastic »...).</summary>
    public string Source { get; set; } = "";

    public GeoPoint Point
    {
        get => new(Latitude, Longitude);
        set => (Latitude, Longitude) = (value.Latitude, value.Longitude);
    }
}
