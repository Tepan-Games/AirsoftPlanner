using AirsoftPlanner.Core.Registration;

namespace AirsoftPlanner.Core.Domain;

/// <summary>Une équipe de joueurs, rattachée à une faction.</summary>
public class Team : Entity
{
    public string Name { get; set; } = "";

    public Guid? FactionId { get; set; }

    /// <summary>Obsolète depuis le format 4 (le chef est un membre) : conservé pour la compatibilité des fichiers.</summary>
    public string LeaderName { get; set; } = "";

    /// <summary>Obsolète depuis le format 4 (le chef est un membre) : conservé pour la compatibilité des fichiers.</summary>
    public string LeaderPhone { get; set; } = "";

    /// <summary>Effectif annoncé, utilisé tant que la liste des membres n'est pas renseignée.</summary>
    public int PlayerCount { get; set; }

    /// <summary>Fréquence radio interne de l'équipe.</summary>
    public string RadioFrequency { get; set; } = "";

    public string Notes { get; set; } = "";

    public RegistrationStatus Status { get; set; }

    /// <summary>Date de la demande d'inscription (ordre de la liste d'attente).</summary>
    public DateTimeOffset? RegisteredAt { get; set; }

    /// <summary>Somme due fixée à la main (remise, forfait), ou null pour effectif × tarif de l'OP.</summary>
    public decimal? AmountDueOverride { get; set; }

    /// <summary>Identifiants des appareils GPS de l'équipe (Traccar Client, nœud Meshtastic...), séparés par des virgules.</summary>
    public string GpsDeviceIds { get; set; } = "";
}
