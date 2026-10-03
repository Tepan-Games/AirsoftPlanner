namespace AirsoftPlanner.Core.Domain;

/// <summary>Une équipe de joueurs, rattachée à une faction.</summary>
public class Team : Entity
{
    public string Name { get; set; } = "";

    public Guid? FactionId { get; set; }

    public string LeaderName { get; set; } = "";

    public string LeaderPhone { get; set; } = "";

    public int PlayerCount { get; set; }

    public string Notes { get; set; } = "";
}
