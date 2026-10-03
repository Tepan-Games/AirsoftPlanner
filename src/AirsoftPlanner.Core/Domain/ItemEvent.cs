namespace AirsoftPlanner.Core.Domain;

public enum ItemEventKind
{
    /// <summary>Placé sur le terrain par l'orga (position connue).</summary>
    Placed,

    /// <summary>Récupéré par une équipe.</summary>
    PickedUp,

    /// <summary>Passé d'une équipe à une autre (capture, échange).</summary>
    Transferred,

    /// <summary>Déposé ou abandonné sur le terrain par une équipe.</summary>
    Dropped,

    /// <summary>Rendu à l'orga : plus rien à récupérer sur le terrain.</summary>
    Returned,

    /// <summary>Perdu : dernière position connue à fouiller.</summary>
    Lost,
}

/// <summary>Événement de la vie d'un objet d'objectif (qui l'a, où, depuis quand), pour le suivre et le récupérer.</summary>
public class ItemEvent : Entity
{
    public Guid ItemId { get; set; }

    public ItemEventKind Kind { get; set; }

    /// <summary>Équipe qui détient l'objet après l'événement (récupération, transfert).</summary>
    public Guid? TeamId { get; set; }

    public bool HasLocation { get; set; }

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public DateTimeOffset At { get; set; }

    public string Notes { get; set; } = "";

    public GeoPoint? Location
    {
        get => HasLocation ? new GeoPoint(Latitude, Longitude) : null;
        set => (HasLocation, Latitude, Longitude) = value is { } p ? (true, p.Latitude, p.Longitude) : (false, 0, 0);
    }
}
