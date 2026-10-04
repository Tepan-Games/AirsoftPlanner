namespace AirsoftPlanner.Core.Domain;

/// <summary>Membre de l'organisation de l'OP (PC, arbitres, logistique, secours...).</summary>
public class Organizer : Entity
{
    public string Name { get; set; } = "";

    /// <summary>Rôle pendant l'OP : directeur de jeu, arbitre, secouriste, logistique...</summary>
    public string Role { get; set; } = "";

    public string Phone { get; set; } = "";

    public string RadioFrequency { get; set; } = "";

    public string Email { get; set; } = "";

    public string Notes { get; set; } = "";

    public int SortOrder { get; set; }

    /// <summary>Code d'enrôlement de l'application Android pour le téléphone de cet orga.</summary>
    public string EnrollmentCode { get; set; } = "";
}
