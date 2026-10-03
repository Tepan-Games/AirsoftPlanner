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

    /// <summary>Les équipes affectées dépassent l'effectif maximum de la mission.</summary>
    TooManyPlayers,

    /// <summary>Le stock d'un élément de jeu ne suffit pas (au total s'il est consommable, en simultané sinon).</summary>
    ItemShortage,
}

/// <summary>Ressources de l'OP utiles à l'analyse : effectif de chaque équipe, stock de matériel de jeu.</summary>
public record ScheduleResources(IReadOnlyDictionary<Guid, int> TeamSizes, IReadOnlyDictionary<Guid, GameItem> Items)
{
    public static ScheduleResources None { get; } = new(new Dictionary<Guid, int>(), new Dictionary<Guid, GameItem>());
}

/// <param name="MissionId">Mission concernée.</param>
/// <param name="OtherMissionId">Autre mission en cause (chevauchement, prérequis), le cas échéant.</param>
/// <param name="TeamId">Équipe en cause (chevauchement), le cas échéant.</param>
/// <param name="ItemId">Élément de jeu en cause (stock insuffisant), le cas échéant.</param>
public record ScheduleIssue(Guid MissionId, ScheduleIssueKind Kind, Guid? OtherMissionId = null, Guid? TeamId = null, Guid? ItemId = null);

/// <summary>Détecte les incohérences du planning. Les missions désactivées sont ignorées.</summary>
public static class ScheduleAnalyzer
{
    public static IReadOnlyList<ScheduleIssue> Analyze(IReadOnlyCollection<Mission> missions, int operationStartMinutes,
        int operationEndMinutes, ScheduleResources? resources = null)
    {
        resources ??= ScheduleResources.None;
        var issues = new List<ScheduleIssue>();
        var enabled = missions.Where(m => m.IsEnabled).ToList();
        var byId = missions.ToDictionary(m => m.Id);

        foreach (var mission in enabled)
        {
            if (mission.TeamIds.Count == 0)
                issues.Add(new ScheduleIssue(mission.Id, ScheduleIssueKind.NoTeam));

            if (mission.StartMinutes < operationStartMinutes || mission.EndMinutes > operationEndMinutes)
                issues.Add(new ScheduleIssue(mission.Id, ScheduleIssueKind.OutsideOperation));

            if (mission.MaxPlayers is { } max && mission.TeamIds.Distinct().Sum(t => resources.TeamSizes.GetValueOrDefault(t)) > max)
                issues.Add(new ScheduleIssue(mission.Id, ScheduleIssueKind.TooManyPlayers));

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

        issues.AddRange(FindItemShortages(enabled, resources.Items));
        return issues;
    }

    /// <summary>Quantité de chaque élément de jeu utilisée par l'ensemble des missions actives.</summary>
    public static IReadOnlyDictionary<Guid, int> TotalItemUse(IEnumerable<Mission> missions) =>
        missions.Where(m => m.IsEnabled)
            .SelectMany(m => m.Items)
            .GroupBy(u => u.ItemId)
            .ToDictionary(g => g.Key, g => g.Sum(u => u.Quantity));

    private static IEnumerable<ScheduleIssue> FindItemShortages(List<Mission> enabled, IReadOnlyDictionary<Guid, GameItem> items)
    {
        foreach (var (itemId, item) in items)
        {
            var users = enabled
                .Select(m => (Mission: m, Quantity: m.Items.Where(u => u.ItemId == itemId).Sum(u => u.Quantity)))
                .Where(x => x.Quantity > 0)
                .ToList();

            if (item.IsConsumable)
            {
                // Consommable : le total sur toute l'OP ne doit pas dépasser le stock.
                if (users.Sum(x => x.Quantity) > item.Quantity)
                    foreach (var (mission, _) in users)
                        yield return new ScheduleIssue(mission.Id, ScheduleIssueKind.ItemShortage, ItemId: itemId);
                continue;
            }

            // Réutilisable : à aucun moment les missions simultanées ne doivent en utiliser plus que le stock.
            foreach (var (mission, _) in users)
            {
                var overloaded = users
                    .Where(x => x.Mission.StartMinutes >= mission.StartMinutes && x.Mission.StartMinutes < mission.EndMinutes)
                    .Select(x => x.Mission.StartMinutes)
                    .Any(instant => users
                        .Where(x => x.Mission.StartMinutes <= instant && instant < x.Mission.EndMinutes)
                        .Sum(x => x.Quantity) > item.Quantity);
                if (overloaded)
                    yield return new ScheduleIssue(mission.Id, ScheduleIssueKind.ItemShortage, ItemId: itemId);
            }
        }
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
