using AirsoftPlanner.Core.Domain;

namespace AirsoftPlanner.Core.Planning;

public enum ScheduleIssueKind
{
    /// <summary>La mission n'est confiée à aucune équipe.</summary>
    NoTeam,

    /// <summary>Une équipe a deux missions en même temps.</summary>
    TeamOverlap,

    /// <summary>La mission commence avant la fin d'un de ses prérequis.</summary>
    PredecessorNotFinished,

    /// <summary>Un prérequis est désactivé : la mission ne pourra pas s'enchaîner comme prévu.</summary>
    PredecessorDisabled,

    /// <summary>Les prérequis forment une boucle (A après B, B après A).</summary>
    DependencyCycle,

    /// <summary>La mission déborde des horaires de l'OP.</summary>
    OutsideOperation,
}

/// <param name="MissionId">Mission concernée.</param>
/// <param name="OtherMissionId">Autre mission en cause (chevauchement, prérequis), le cas échéant.</param>
/// <param name="TeamId">Équipe en cause (chevauchement), le cas échéant.</param>
public record ScheduleIssue(Guid MissionId, ScheduleIssueKind Kind, Guid? OtherMissionId = null, Guid? TeamId = null);

/// <summary>Détecte les incohérences du planning. Les missions désactivées sont ignorées.</summary>
public static class ScheduleAnalyzer
{
    public static IReadOnlyList<ScheduleIssue> Analyze(IReadOnlyCollection<Mission> missions, int operationStartMinutes, int operationEndMinutes)
    {
        var issues = new List<ScheduleIssue>();
        var enabled = missions.Where(m => m.IsEnabled).ToList();
        var byId = missions.ToDictionary(m => m.Id);

        foreach (var mission in enabled)
        {
            if (mission.TeamIds.Count == 0)
                issues.Add(new ScheduleIssue(mission.Id, ScheduleIssueKind.NoTeam));

            if (mission.StartMinutes < operationStartMinutes || mission.EndMinutes > operationEndMinutes)
                issues.Add(new ScheduleIssue(mission.Id, ScheduleIssueKind.OutsideOperation));

            foreach (var predecessorId in mission.PredecessorIds)
            {
                if (!byId.TryGetValue(predecessorId, out var predecessor))
                    continue;
                if (!predecessor.IsEnabled)
                    issues.Add(new ScheduleIssue(mission.Id, ScheduleIssueKind.PredecessorDisabled, predecessor.Id));
                else if (mission.StartMinutes < predecessor.EndMinutes)
                    issues.Add(new ScheduleIssue(mission.Id, ScheduleIssueKind.PredecessorNotFinished, predecessor.Id));
            }
        }

        foreach (var group in enabled.SelectMany(m => m.TeamIds.Distinct(), (mission, team) => (mission, team)).GroupBy(x => x.team))
        {
            var ordered = group.Select(x => x.mission).OrderBy(m => m.StartMinutes).ToList();
            for (var i = 0; i < ordered.Count; i++)
            for (var j = i + 1; j < ordered.Count && ordered[j].StartMinutes < ordered[i].EndMinutes; j++)
            {
                issues.Add(new ScheduleIssue(ordered[i].Id, ScheduleIssueKind.TeamOverlap, ordered[j].Id, group.Key));
                issues.Add(new ScheduleIssue(ordered[j].Id, ScheduleIssueKind.TeamOverlap, ordered[i].Id, group.Key));
            }
        }

        foreach (var missionId in FindCycleMembers(byId))
            issues.Add(new ScheduleIssue(missionId, ScheduleIssueKind.DependencyCycle));

        return issues;
    }

    /// <summary>Indique si ajouter <paramref name="predecessorId"/> comme prérequis de <paramref name="missionId"/> créerait une boucle.</summary>
    public static bool WouldCreateCycle(IReadOnlyCollection<Mission> missions, Guid missionId, Guid predecessorId)
    {
        if (missionId == predecessorId)
            return true;

        // Boucle si la mission est déjà (indirectement) un prérequis du futur prérequis.
        var byId = missions.ToDictionary(m => m.Id);
        var visited = new HashSet<Guid>();
        var stack = new Stack<Guid>([predecessorId]);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current == missionId)
                return true;
            if (!visited.Add(current) || !byId.TryGetValue(current, out var mission))
                continue;
            foreach (var next in mission.PredecessorIds)
                stack.Push(next);
        }

        return false;
    }

    private static IEnumerable<Guid> FindCycleMembers(IReadOnlyDictionary<Guid, Mission> byId)
    {
        // Parcours en profondeur : une mission rencontrée alors qu'elle est en cours d'exploration ferme une boucle.
        var state = new Dictionary<Guid, int>(); // 1 = en cours, 2 = terminé
        var path = new List<Guid>();
        var members = new HashSet<Guid>();

        void Visit(Guid id)
        {
            state[id] = 1;
            path.Add(id);
            foreach (var next in byId[id].PredecessorIds.Where(byId.ContainsKey))
            {
                if (state.GetValueOrDefault(next) == 1)
                    members.UnionWith(path.Skip(path.IndexOf(next)));
                else if (state.GetValueOrDefault(next) == 0)
                    Visit(next);
            }

            path.RemoveAt(path.Count - 1);
            state[id] = 2;
        }

        foreach (var id in byId.Keys.Where(id => state.GetValueOrDefault(id) == 0))
            Visit(id);
        return members;
    }
}
