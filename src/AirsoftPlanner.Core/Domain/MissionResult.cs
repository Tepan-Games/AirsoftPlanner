using AirsoftPlanner.Core.Localization;

namespace AirsoftPlanner.Core.Domain;

/// <summary>Résultat d'une mission, saisi par l'orga (un seul pour toutes les équipes de la mission).</summary>
/// <remarks>Stocké en texte ; NotEvaluated vient en premier (missions créées avant cette option).</remarks>
public enum MissionResult
{
    NotEvaluated,
    Success,
    Partial,
    Failure,
}

/// <summary>Mission prévue selon le résultat d'une autre (suite si réussite, plan de repli si échec).</summary>
/// <remarks>Stocké en texte ; None vient en premier.</remarks>
public enum MissionCondition
{
    None,

    /// <summary>Si l'autre mission est réussie, même partiellement.</summary>
    IfSuccess,

    /// <summary>Si l'autre mission est échouée.</summary>
    IfFailure,
}

public static class MissionResults
{
    public static IReadOnlyList<MissionResult> All { get; } =
        [MissionResult.NotEvaluated, MissionResult.Success, MissionResult.Partial, MissionResult.Failure];

    public static IReadOnlyList<MissionCondition> Conditions { get; } = [MissionCondition.None, MissionCondition.IfSuccess, MissionCondition.IfFailure];

    public static string Label(MissionResult result) => result switch
    {
        MissionResult.Success => L.T("resultat_reussie"),
        MissionResult.Partial => L.T("resultat_partielle"),
        MissionResult.Failure => L.T("resultat_echouee"),
        _ => L.T("resultat_non_evaluee"),
    };

    /// <summary>Repère court affiché sur la frise et dans les listes.</summary>
    public static string Symbol(MissionResult result) => result switch
    {
        MissionResult.Success => "✔",
        MissionResult.Partial => "◐",
        MissionResult.Failure => "✘",
        _ => "",
    };

    public static string ConditionLabel(MissionCondition condition) => condition switch
    {
        MissionCondition.IfSuccess => L.T("condition_si_reussie"),
        MissionCondition.IfFailure => L.T("condition_si_echouee"),
        _ => L.T("condition_toujours"),
    };

    /// <summary>Points rapportés par la mission selon son résultat (0 tant qu'elle n'est pas évaluée).</summary>
    public static int Points(Mission mission) => mission.Result switch
    {
        MissionResult.Success => mission.SuccessPoints,
        MissionResult.Partial => mission.PartialPoints,
        MissionResult.Failure => mission.FailurePoints,
        _ => 0,
    };

    /// <summary>
    /// Condition d'une mission : vraie (à jouer), fausse (caduque) ou null tant que la mission dont elle dépend
    /// n'est pas évaluée. Sans condition, ou si cette mission n'existe plus : toujours à jouer.
    /// </summary>
    public static bool? IsConditionMet(Mission mission, IEnumerable<Mission> missions)
    {
        if (mission.Condition == MissionCondition.None || mission.ConditionMissionId is not { } id
            || missions.FirstOrDefault(m => m.Id == id) is not { } other)
            return true;
        if (other.Result == MissionResult.NotEvaluated)
            return null;
        var succeeded = other.Result is MissionResult.Success or MissionResult.Partial;
        return mission.Condition == MissionCondition.IfSuccess ? succeeded : !succeeded;
    }

    /// <summary>Deux missions alternatives (l'une si réussite, l'autre si échec de la même mission) : jamais jouées toutes les deux.</summary>
    public static bool AreAlternatives(Mission a, Mission b) =>
        a.ConditionMissionId is { } id && b.ConditionMissionId == id
        && a.Condition != MissionCondition.None && b.Condition != MissionCondition.None && a.Condition != b.Condition;
}
