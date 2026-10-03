namespace AirsoftPlanner.Core.Domain;

/// <summary>Un camp de l'OP, qui regroupe plusieurs équipes.</summary>
public class Faction : Entity
{
    public string Name { get; set; } = "";

    /// <summary>Couleur d'affichage (brassard, carte, frise), au format #RRGGBB.</summary>
    public string Color { get; set; } = "#2E7D32";

    public string Description { get; set; } = "";
}
