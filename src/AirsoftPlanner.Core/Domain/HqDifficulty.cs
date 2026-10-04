using AirsoftPlanner.Core.Gps;
using AirsoftPlanner.Core.Localization;

namespace AirsoftPlanner.Core.Domain;

/// <summary>
/// Niveau de difficulté : ce que l'onglet QG de l'application montre aux joueurs (l'onglet ORGA n'est jamais limité).
/// Réglé pour l'OP, modifiable par faction. Échangé en texte (JSON) et enregistré en texte.
/// </summary>
public enum HqDifficulty
{
    /// <summary>Tout : mission, ordres du QG, radio, points d'intérêt, positions, sur la carte du terrain.</summary>
    Easy,

    /// <summary>Comme Facile, sans la carte : positions, objectifs et points en coordonnées.</summary>
    Medium,

    /// <summary>Seulement les fréquences radio de l'équipe et de son QG.</summary>
    Hard,

    /// <summary>Rien, à part le rappel du niveau.</summary>
    Extreme,
}

public static class HqDifficultyRules
{
    public static IReadOnlyList<HqDifficulty> All { get; } = [HqDifficulty.Easy, HqDifficulty.Medium, HqDifficulty.Hard, HqDifficulty.Extreme];

    /// <summary>Niveau d'une faction : le sien s'il est choisi, sinon celui de l'OP.</summary>
    public static HqDifficulty For(Operation operation, Faction? faction) => faction?.HqDifficulty ?? operation.HqDifficulty;

    /// <summary>Mission, ordres du QG, points d'intérêt et positions (Facile, Moyen).</summary>
    public static bool SharesGame(HqDifficulty level) => level <= HqDifficulty.Medium;

    /// <summary>Fréquences de l'équipe et du QG (tous les niveaux sauf Extrême).</summary>
    public static bool SharesRadio(HqDifficulty level) => level <= HqDifficulty.Hard;

    /// <summary>Partage des positions correspondant au niveau (champ conservé dans le protocole).</summary>
    public static AllyShareMode ShareMode(HqDifficulty level) => level switch
    {
        HqDifficulty.Easy => AllyShareMode.Map,
        HqDifficulty.Medium => AllyShareMode.Coordinates,
        _ => AllyShareMode.None,
    };

    public static string Label(HqDifficulty level) => level switch
    {
        HqDifficulty.Easy => L.T("difficulte_facile"),
        HqDifficulty.Medium => L.T("difficulte_moyen"),
        HqDifficulty.Hard => L.T("difficulte_difficile"),
        _ => L.T("difficulte_extreme"),
    };

    /// <summary>Ce que le joueur reçoit (et ne reçoit pas) à ce niveau : affiché sur le téléphone et dans le logiciel.</summary>
    public static string Explanation(HqDifficulty level) => level switch
    {
        HqDifficulty.Easy => L.T("difficulte_facile_explication"),
        HqDifficulty.Medium => L.T("difficulte_moyen_explication"),
        HqDifficulty.Hard => L.T("difficulte_difficile_explication"),
        _ => L.T("difficulte_extreme_explication"),
    };
}
