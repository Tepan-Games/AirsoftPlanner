namespace AirsoftPlanner.Core.Domain;

/// <summary>
/// Message envoyé au QG (en jeu) ou à l'orga depuis l'application Android (chef d'équipe ou orga) : texte et/ou photo,
/// avec la dernière position connue du téléphone.
/// </summary>
public class PhoneReport : Entity
{
    /// <summary>Équipe (ou orga, voir <see cref="FromOrganizer"/>) du téléphone.</summary>
    public Guid AuthorId { get; set; }

    public bool FromOrganizer { get; set; }

    /// <summary>Destinataire choisi sur le téléphone : le QG (roleplay) ou l'orga.</summary>
    public MessageSender Recipient { get; set; }

    /// <summary>Nom de l'équipe ou de l'orga au moment de l'envoi (affiché même si elle est renommée ensuite).</summary>
    public string Author { get; set; } = "";

    public string DeviceName { get; set; } = "";

    public string Text { get; set; } = "";

    /// <summary>Photo (JPEG réduit par le téléphone), ou null.</summary>
    public byte[]? Photo { get; set; }

    /// <summary>Heure d'envoi sur le téléphone (le message a pu attendre le retour du réseau).</summary>
    public DateTimeOffset SentAt { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    /// <summary>Lu par l'orga sur le PC.</summary>
    public bool IsRead { get; set; }

    /// <summary>Identifiant choisi par le téléphone : un message renvoyé après une coupure n'est enregistré qu'une fois.</summary>
    public Guid ClientId { get; set; }
}
