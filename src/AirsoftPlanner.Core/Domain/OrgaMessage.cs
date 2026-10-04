namespace AirsoftPlanner.Core.Domain;

/// <summary>Destinataires d'un message de l'orga.</summary>
/// <remarks>Stocké en texte ; AllTeams vient en premier (valeur par défaut).</remarks>
public enum MessageTarget
{
    AllTeams,
    Faction,
    Team,
}

/// <summary>
/// Expéditeur affiché sur le téléphone : l'orga (organisation, sécurité, logistique) ou le QG
/// (ordres « en jeu », pour préserver le roleplay).
/// </summary>
/// <remarks>Orga vient en premier : messages envoyés avant cette option.</remarks>
public enum MessageSender
{
    Orga,
    Hq,
}

/// <summary>Nature du message : texte libre de l'orga, ou annonce liée à une mission.</summary>
public enum MessageKind
{
    Text,
    MissionAssigned,
    MissionEnded,
}

/// <summary>Message envoyé par l'orga aux téléphones des équipes (application Android).</summary>
public class OrgaMessage : Entity
{
    public string Text { get; set; } = "";

    public MessageTarget Target { get; set; }

    /// <summary>Faction ou équipe destinataire (selon <see cref="Target"/>).</summary>
    public Guid? TargetId { get; set; }

    public MessageKind Kind { get; set; }

    public MessageSender Sender { get; set; }

    /// <summary>Mission concernée (annonce de mission).</summary>
    public Guid? MissionId { get; set; }

    public DateTimeOffset SentAt { get; set; }

    /// <summary>Le message s'adresse-t-il à cette équipe ?</summary>
    public bool IsFor(Team team) => Target switch
    {
        MessageTarget.AllTeams => true,
        MessageTarget.Faction => team.FactionId is not null && team.FactionId == TargetId,
        MessageTarget.Team => team.Id == TargetId,
        _ => false,
    };
}
