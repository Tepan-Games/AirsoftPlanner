using AirsoftPlanner.Core.Localization;

namespace AirsoftPlanner.Core.Domain;

/// <summary>Déroulement de la partie, déclaré par l'orga et annoncé aux téléphones par une alerte particulière.</summary>
/// <remarks>Stocké et échangé en texte ; NotStarted vient en premier (OP créées avant cette option).</remarks>
public enum GamePhase
{
    NotStarted,
    Running,
    Paused,
    Ended,
}

/// <summary>Changement de phase de la partie (historique, RETEX).</summary>
public class GamePhaseEvent : Entity
{
    public GamePhase Phase { get; set; }

    public DateTimeOffset At { get; set; }
}

public static class GamePhases
{
    public static string Label(GamePhase phase) => phase switch
    {
        GamePhase.Running => L.T("partie_en_cours"),
        GamePhase.Paused => L.T("jeu_en_pause"),
        GamePhase.Ended => L.T("fin_de_partie"),
        _ => L.T("partie_pas_encore_commencee"),
    };

    public static string Symbol(GamePhase phase) => phase switch
    {
        GamePhase.Running => "▶",
        GamePhase.Paused => "⏸",
        GamePhase.Ended => "⏹",
        _ => "◷",
    };

    /// <summary>Annonce faite aux joueurs lors du passage à cette phase (notification et bandeau du téléphone).</summary>
    public static string Announcement(GamePhase phase, GamePhase previous) => phase switch
    {
        GamePhase.Running when previous == GamePhase.Paused => L.T("annonce_reprise_du_jeu"),
        GamePhase.Running => L.T("annonce_debut_de_partie"),
        GamePhase.Paused => L.T("annonce_jeu_en_pause"),
        GamePhase.Ended => L.T("annonce_fin_de_partie"),
        _ => L.T("partie_pas_encore_commencee"),
    };

    /// <summary>Titre de l'alerte : « DÉBUT DE PARTIE », « JEU EN PAUSE », « REPRISE DU JEU », « FIN DE PARTIE ».</summary>
    public static string AlertTitle(GamePhase phase, GamePhase previous) => (phase switch
    {
        GamePhase.Running when previous == GamePhase.Paused => L.T("reprise_du_jeu"),
        GamePhase.Running => L.T("debut_de_partie"),
        GamePhase.Paused => L.T("jeu_en_pause"),
        GamePhase.Ended => L.T("fin_de_partie"),
        _ => L.T("partie_pas_encore_commencee"),
    }).ToUpper(L.Culture);
}
