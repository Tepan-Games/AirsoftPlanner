namespace AirsoftPlanner.Core.Domain;

/// <summary>Un joueur d'une équipe.</summary>
public class TeamMember : Entity
{
    public Guid TeamId { get; set; }

    public string FirstName { get; set; } = "";

    public string LastName { get; set; } = "";

    /// <summary>Pseudo / indicatif radio.</summary>
    public string Callsign { get; set; } = "";

    /// <summary>Rôle dans l'équipe (radio, médic, tireur de précision...).</summary>
    public string Role { get; set; } = "";

    public string Phone { get; set; } = "";

    public string Email { get; set; } = "";

    public bool IsLeader { get; set; }

    public int SortOrder { get; set; }
}
