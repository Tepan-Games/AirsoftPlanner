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
}
