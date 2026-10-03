namespace AirsoftPlanner.Core.Domain;

/// <summary>Une OP (partie organisée). Un fichier d'OP contient exactement une opération.</summary>
public class Operation : Entity
{
    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    public string Location { get; set; } = "";

    public DateTimeOffset StartsAt { get; set; }

    public DateTimeOffset EndsAt { get; set; }
}
