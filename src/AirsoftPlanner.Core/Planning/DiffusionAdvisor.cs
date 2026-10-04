using AirsoftPlanner.Core.Domain;

namespace AirsoftPlanner.Core.Planning;

public enum DiffusionAdvice
{
    /// <summary>La mission diffusée a dépassé son heure de fin : la terminer (et diffuser la suivante) ?</summary>
    EndCurrent,

    /// <summary>Aucune mission diffusée et la prochaine approche (ou a commencé) : la diffuser ?</summary>
    SendNext,
}

/// <summary>Question à poser à l'orga pour une équipe.</summary>
/// <param name="Current">Mission actuellement diffusée à l'équipe.</param>
/// <param name="Next">Mission suivante au programme de l'équipe.</param>
public record DiffusionSuggestion(Guid TeamId, DiffusionAdvice Advice, Mission? Current, Mission? Next);

/// <summary>
/// Aide à la diffusion des missions : rien n'est envoyé automatiquement aux téléphones, mais le logiciel
/// signale à l'orga le moment de terminer une mission ou d'en diffuser une nouvelle.
/// </summary>
public static class DiffusionAdvisor
{
    /// <summary>Délai avant le début prévu d'une mission à partir duquel sa diffusion est proposée.</summary>
    public const double LeadMinutes = 15;

    /// <param name="published">Mission diffusée à l'équipe (null : aucune).</param>
    /// <param name="completed">Missions déjà terminées par l'orga pour cette équipe.</param>
    public static DiffusionSuggestion? Advise(Guid teamId, IEnumerable<Mission> missions, Guid? published, IReadOnlyCollection<Guid> completed,
        double nowMinutes, double leadMinutes = LeadMinutes)
    {
        var all = missions as IReadOnlyCollection<Mission> ?? missions.ToList();
        // Mission conditionnelle : proposée seulement quand le résultat de la mission dont elle dépend la rend jouable.
        var program = all
            .Where(m => m.IsEnabled && m.TeamIds.Contains(teamId) && !completed.Contains(m.Id) && MissionResults.IsConditionMet(m, all) == true)
            .OrderBy(m => m.StartMinutes)
            .ThenBy(m => m.Name, StringComparer.CurrentCulture)
            .ToList();
        var current = published is { } id ? all.FirstOrDefault(m => m.Id == id) : null;

        if (current is not null)
        {
            if (nowMinutes < current.EndMinutes)
                return null;
            var next = program.FirstOrDefault(m => m.Id != current.Id && m.StartMinutes >= current.StartMinutes)
                       ?? program.FirstOrDefault(m => m.Id != current.Id && m.EndMinutes > nowMinutes);
            return new DiffusionSuggestion(teamId, DiffusionAdvice.EndCurrent, current, next);
        }

        // Prochaine mission pas encore finie selon le programme (une mission dont l'heure est passée se diffuse à la main).
        var upcoming = program.FirstOrDefault(m => m.EndMinutes > nowMinutes);
        return upcoming is not null && upcoming.StartMinutes - nowMinutes <= leadMinutes
            ? new DiffusionSuggestion(teamId, DiffusionAdvice.SendNext, null, upcoming)
            : null;
    }
}
