namespace AirsoftPlanner.Core.Domain;

public enum OutReason
{
    /// <summary>Blessure réelle (hors jeu) : à signaler en priorité.</summary>
    RealInjury,

    /// <summary>Pause, fatigue, repas.</summary>
    Rest,

    /// <summary>Problème de réplique, de batterie, de protection...</summary>
    Equipment,

    /// <summary>Sanction de l'orga (fair-play, sécurité).</summary>
    Sanction,

    /// <summary>Abandon ou départ anticipé.</summary>
    Abandon,

    Other,
}

/// <summary>
/// Un joueur (ou plusieurs, si les membres ne sont pas saisis) sort du jeu ou y revient.
/// Permet de connaître l'effectif réellement présent de chaque équipe tout au long de l'OP.
/// </summary>
public class PlayerStatusEvent : Entity
{
    public Guid TeamId { get; set; }

    /// <summary>Joueur concerné, ou null pour un nombre de joueurs non nommés.</summary>
    public Guid? MemberId { get; set; }

    /// <summary>Vrai : sortie du jeu ; faux : retour en jeu.</summary>
    public bool IsOut { get; set; }

    public OutReason Reason { get; set; }

    /// <summary>Nombre de joueurs concernés (pour les joueurs non nommés).</summary>
    public int Players { get; set; } = 1;

    public DateTimeOffset At { get; set; }

    public string Notes { get; set; } = "";
}
