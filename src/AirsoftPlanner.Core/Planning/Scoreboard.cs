using AirsoftPlanner.Core.Domain;

namespace AirsoftPlanner.Core.Planning;

/// <summary>Points d'une faction ou d'une équipe : total et missions évaluées.</summary>
public record ScoreLine(Guid Id, string Name, string Color, int Points, int Success, int Partial, int Failure)
{
    public int Evaluated => Success + Partial + Failure;
}

/// <summary>
/// Score de l'OP d'après le résultat des missions : chaque équipe engagée sur une mission en reçoit les points ;
/// une faction les reçoit une fois par mission, même si plusieurs de ses équipes y participent.
/// </summary>
public static class Scoreboard
{
    public static IReadOnlyList<ScoreLine> ByFaction(IEnumerable<Mission> missions, IEnumerable<Faction> factions, IEnumerable<Team> teams) =>
        ByFaction(missions, factions.Select(f => (f.Id, f.Name, f.Color)), teams);

    public static IReadOnlyList<ScoreLine> ByFaction(IEnumerable<Mission> missions, IEnumerable<(Guid Id, string Name, string Color)> factions,
        IEnumerable<Team> teams)
    {
        var factionOf = teams.Where(t => t.FactionId is not null).ToDictionary(t => t.Id, t => t.FactionId!.Value);
        var evaluated = Evaluated(missions);
        return factions
            .Select(f => Line(f.Id, f.Name, f.Color, evaluated.Where(m => m.TeamIds.Any(t => factionOf.GetValueOrDefault(t) == f.Id))))
            .OrderByDescending(l => l.Points)
            .ThenBy(l => l.Name, StringComparer.CurrentCulture)
            .ToList();
    }

    public static IReadOnlyList<ScoreLine> ByTeam(IEnumerable<Mission> missions, IEnumerable<Team> teams, Func<Team, string>? color = null)
    {
        var evaluated = Evaluated(missions);
        return teams
            .Select(t => Line(t.Id, t.Name, color?.Invoke(t) ?? "", evaluated.Where(m => m.TeamIds.Contains(t.Id))))
            .OrderByDescending(l => l.Points)
            .ThenBy(l => l.Name, StringComparer.CurrentCulture)
            .ToList();
    }

    private static List<Mission> Evaluated(IEnumerable<Mission> missions) =>
        missions.Where(m => m.IsEnabled && m.Result != MissionResult.NotEvaluated).ToList();

    private static ScoreLine Line(Guid id, string name, string color, IEnumerable<Mission> missions)
    {
        var list = missions.ToList();
        return new ScoreLine(id, name, color, list.Sum(MissionResults.Points),
            list.Count(m => m.Result == MissionResult.Success),
            list.Count(m => m.Result == MissionResult.Partial),
            list.Count(m => m.Result == MissionResult.Failure));
    }
}
