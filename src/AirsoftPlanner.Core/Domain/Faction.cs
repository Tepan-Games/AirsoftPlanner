namespace AirsoftPlanner.Core.Domain;

/// <summary>Un camp de l'OP, qui regroupe plusieurs équipes.</summary>
public class Faction : Entity
{
    public string Name { get; set; } = "";

    /// <summary>Couleur d'affichage (brassard, carte, frise), au format #RRGGBB.</summary>
    public string Color { get; set; } = "#2E7D32";

    public string Description { get; set; } = "";

    /// <summary>Effectif minimum souhaité (0 = pas de minimum).</summary>
    public int MinPlayers { get; set; }

    /// <summary>Effectif maximum accepté (0 = pas de limite).</summary>
    public int MaxPlayers { get; set; }

    /// <summary>Couleur de brassard au format #RRGGBB, ou vide s'il n'y a pas de brassard.</summary>
    public string ArmbandColor { get; set; } = "";

    /// <summary>Tenue ou camouflage imposé (ex. « Multicam », « CE », « civil »), vide si libre.</summary>
    public string Uniform { get; set; } = "";

    /// <summary>Fréquence radio de commandement de la faction.</summary>
    public string RadioFrequency { get; set; } = "";

    /// <summary>Équipe qui commande la faction.</summary>
    public Guid? CommandTeamId { get; set; }
}
