namespace AirsoftPlanner.Core.Domain;

/// <summary>
/// Suivi de la diffusion du package d'une équipe (règles + ordres de mission initiaux) :
/// génération, envoi et accusé de réception pointés par l'orga.
/// </summary>
public class TeamPackage : Entity
{
    public Guid TeamId { get; set; }

    public DateTimeOffset? GeneratedAt { get; set; }

    /// <summary>Empreinte du contenu au moment de la génération, pour détecter un package devenu obsolète.</summary>
    public string Fingerprint { get; set; } = "";

    /// <summary>Dossier où le package a été généré.</summary>
    public string OutputPath { get; set; } = "";

    public DateTimeOffset? SentAt { get; set; }

    public DateTimeOffset? ReceivedAt { get; set; }

    /// <summary>Qui a confirmé la réception (chef d'équipe...).</summary>
    public string ReceivedBy { get; set; } = "";
}
