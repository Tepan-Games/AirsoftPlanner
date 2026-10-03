using AirsoftPlanner.Core.Domain;

namespace AirsoftPlanner.Core.Planning;

/// <param name="MissionId">Mission décalée.</param>
/// <param name="OldStart">Début avant le retard.</param>
/// <param name="NewStart">Début après décalage.</param>
public record ScheduleChange(Guid MissionId, int OldStart, int NewStart)
{
    public int Shift => NewStart - OldStart;
}

/// <param name="Changes">Missions décalées (dans l'ordre chronologique).</param>
/// <param name="DisabledMissionIds">Missions optionnelles désactivées pour absorber le retard.</param>
/// <param name="EssentialDelayMinutes">Somme des décalages subis par les missions essentielles (hors mission en retard).</param>
/// <param name="MaxEssentialDelayMinutes">Plus grand décalage subi par une mission essentielle (hors mission en retard).</param>
/// <param name="OverflowMinutes">Dépassement de la fin de l'OP par la dernière mission, s'il y en a.</param>
public record DelayPlan(
    IReadOnlyList<ScheduleChange> Changes,
    IReadOnlyList<Guid> DisabledMissionIds,
    int EssentialDelayMinutes,
    int MaxEssentialDelayMinutes,
    int OverflowMinutes);

/// <param name="Mission">Mission optionnelle qu'on propose de désactiver.</param>
/// <param name="GainMinutes">Retard évité sur les missions essentielles si on la désactive.</param>
public record OptionalCut(Mission Mission, int GainMinutes);

/// <summary>
/// Répercute le retard d'une mission sur le reste du planning et cherche quelles missions optionnelles
/// désactiver pour l'absorber. Les missions ne sont jamais avancées : seulement retardées si nécessaire.
/// </summary>
public static class DelayPlanner
{
    /// <summary>Calcule le décalage en cascade provoqué par le retard d'une mission.</summary>
    /// <param name="missions">Toutes les missions de l'OP.</param>
    /// <param name="delayedMissionId">Mission en retard.</param>
    /// <param name="delayMinutes">Retard à répercuter (positif).</param>
    /// <param name="operationEndMinutes">Fin de l'OP, pour mesurer un éventuel dépassement.</param>
    /// <param name="disabled">Missions supplémentaires considérées comme désactivées (pistes d'optimisation).</param>
    public static DelayPlan Plan(IReadOnlyCollection<Mission> missions, Guid delayedMissionId, int delayMinutes,
        int operationEndMinutes, IReadOnlySet<Guid>? disabled = null)
    {
        disabled ??= new HashSet<Guid>();
        var active = missions.Where(m => m.IsEnabled && !disabled.Contains(m.Id)).ToList();
        var delayed = active.FirstOrDefault(m => m.Id == delayedMissionId)
                      ?? throw new ArgumentException("La mission en retard doit être active.", nameof(delayedMissionId));

        var newStart = active.ToDictionary(m => m.Id, m => m.StartMinutes);
        newStart[delayed.Id] = delayed.StartMinutes + Math.Max(0, delayMinutes);
        int NewEnd(Mission m) => newStart[m.Id] + m.DurationMinutes;

        // Les missions antérieures à la mission en retard ne bougent pas ; les suivantes sont traitées dans l'ordre prévu.
        var affected = active
            .Where(m => m.Id != delayed.Id && m.StartMinutes >= delayed.StartMinutes)
            .OrderBy(m => m.StartMinutes).ThenBy(m => m.EndMinutes)
            .ToList();
        var processed = new List<Mission> { delayed };
        var byId = active.ToDictionary(m => m.Id);

        foreach (var mission in affected)
        {
            var required = mission.StartMinutes;

            // Prérequis : attendre la fin (décalée) de chaque mission préalable encore active.
            foreach (var predecessorId in mission.PredecessorIds)
                if (byId.TryGetValue(predecessorId, out var predecessor))
                    required = Math.Max(required, NewEnd(predecessor));

            // Équipes : une mission qui suivait (sans chevauchement prévu) une autre mission de la même équipe la suit toujours.
            foreach (var before in processed.Where(p => p.TeamIds.Intersect(mission.TeamIds).Any() && p.EndMinutes <= mission.StartMinutes))
                required = Math.Max(required, NewEnd(before));

            newStart[mission.Id] = required;
            processed.Add(mission);
        }

        var changes = active
            .Where(m => newStart[m.Id] != m.StartMinutes)
            .OrderBy(m => newStart[m.Id])
            .Select(m => new ScheduleChange(m.Id, m.StartMinutes, newStart[m.Id]))
            .ToList();
        var essentialShifts = changes
            .Where(c => c.MissionId != delayed.Id && byId[c.MissionId].IsEssential)
            .Select(c => c.Shift)
            .ToList();
        var lastEnd = active.Count == 0 ? 0 : active.Max(NewEnd);

        return new DelayPlan(changes, disabled.ToList(), essentialShifts.Sum(), essentialShifts.DefaultIfEmpty(0).Max(),
            Math.Max(0, lastEnd - operationEndMinutes));
    }

    /// <summary>Missions optionnelles touchées par le retard, avec le retard qu'on éviterait en désactivant chacune.</summary>
    public static IReadOnlyList<OptionalCut> SuggestCuts(IReadOnlyCollection<Mission> missions, Guid delayedMissionId, int delayMinutes,
        int operationEndMinutes, IReadOnlySet<Guid>? alreadyDisabled = null)
    {
        alreadyDisabled ??= new HashSet<Guid>();
        var baseline = Plan(missions, delayedMissionId, delayMinutes, operationEndMinutes, alreadyDisabled);
        var delayedStart = missions.First(m => m.Id == delayedMissionId).StartMinutes;

        return missions
            .Where(m => m.IsEnabled && !m.IsEssential && m.Id != delayedMissionId && !alreadyDisabled.Contains(m.Id)
                        && m.StartMinutes >= delayedStart)
            .Select(m =>
            {
                var plan = Plan(missions, delayedMissionId, delayMinutes, operationEndMinutes, new HashSet<Guid>(alreadyDisabled) { m.Id });
                return new OptionalCut(m, Score(baseline) - Score(plan));
            })
            .Where(cut => cut.GainMinutes > 0)
            .OrderByDescending(cut => cut.GainMinutes)
            .ToList();
    }

    /// <summary>
    /// Désactive une à une les missions optionnelles les plus utiles tant que cela réduit le retard des missions
    /// essentielles (approche gloutonne, suffisante à l'échelle d'une OP).
    /// </summary>
    public static DelayPlan Optimize(IReadOnlyCollection<Mission> missions, Guid delayedMissionId, int delayMinutes, int operationEndMinutes)
    {
        var disabled = new HashSet<Guid>();
        while (SuggestCuts(missions, delayedMissionId, delayMinutes, operationEndMinutes, disabled).FirstOrDefault() is { } best)
            disabled.Add(best.Mission.Id);
        return Plan(missions, delayedMissionId, delayMinutes, operationEndMinutes, disabled);
    }

    /// <summary>Ce qu'on cherche à minimiser : le retard des missions essentielles, et surtout le dépassement de la fin de l'OP.</summary>
    private static int Score(DelayPlan plan) => plan.EssentialDelayMinutes + plan.OverflowMinutes * 10;
}
