using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Core.Geo;
using AirsoftPlanner.Core.Localization;

namespace AirsoftPlanner.Core.Retex;

/// <summary>Mission d'une équipe : horaire prévu, diffusion et fin décidées par l'orga.</summary>
/// <param name="DiffusionDelayMinutes">Retard de la diffusion sur le début prévu (négatif : diffusée en avance).</param>
public record MissionOutcome(Mission Mission, DateTimeOffset PlannedStart, DateTimeOffset PlannedEnd, DateTimeOffset? PublishedAt,
    DateTimeOffset? EndedAt, bool Completed)
{
    public double? DiffusionDelayMinutes => PublishedAt is { } at ? (at - PlannedStart).TotalMinutes : null;

    /// <summary>Durée réelle entre la diffusion et la fin, si les deux sont connues.</summary>
    public double? ActualMinutes => PublishedAt is { } start && EndedAt is { } end ? (end - start).TotalMinutes : null;
}

/// <summary>Fait marquant de l'OP (chronologie du RETEX).</summary>
public record RetexEvent(DateTimeOffset At, string Team, string Category, string Text)
{
    /// <summary>Jour et heure locale (« sam. 10:20 »).</summary>
    public string Time => At.LocalDateTime.ToString("ddd HH:mm", AirsoftPlanner.Core.Localization.L.Culture);
}

/// <summary>Bilan d'une équipe.</summary>
public record TeamRetex(
    Team Team,
    IReadOnlyList<MissionOutcome> Missions,
    double DistanceKm,
    int PositionCount,
    DateTimeOffset? FirstPosition,
    DateTimeOffset? LastPosition,
    int PlayersOut,
    IReadOnlyList<OrgaMessage> Messages,
    IReadOnlyList<RetexEvent> Events)
{
    /// <summary>Messages envoyés à l'orga depuis le téléphone de l'équipe.</summary>
    public IReadOnlyList<PhoneReport> Reports { get; init; } = [];

    public int MissionsPlanned => Missions.Count;

    public int MissionsPublished => Missions.Count(m => m.PublishedAt is not null);

    public int MissionsCompleted => Missions.Count(m => m.Completed);

    /// <summary>Points gagnés par l'équipe d'après le résultat de ses missions.</summary>
    public int Points => Missions.Where(m => m.Mission.Result != MissionResult.NotEvaluated).Sum(m => MissionResults.Points(m.Mission));

    public IReadOnlyList<GeoPoint> Trail { get; init; } = [];
}

/// <summary>Bilan de l'OP : une fiche par équipe et la chronologie de tous les faits marquants.</summary>
public record OperationRetex(IReadOnlyList<TeamRetex> Teams, IReadOnlyList<RetexEvent> Timeline);

/// <summary>Construit le RETEX à partir de ce qui a été enregistré pendant l'OP (positions, messages, événements).</summary>
public static class RetexBuilder
{
    /// <summary>Distance entre deux positions au-delà de laquelle on considère un saut GPS (non compté).</summary>
    private const double MaxJumpMeters = 2000;

    public static OperationRetex Build(
        IReadOnlyList<Team> teams,
        IReadOnlyList<Mission> missions,
        IReadOnlyList<TeamPosition> positions,
        IReadOnlyList<OrgaMessage> messages,
        IReadOnlyList<ItemEvent> itemEvents,
        IReadOnlyList<PlayerStatusEvent> playerEvents,
        Func<int, DateTimeOffset> missionTime,
        Func<Guid, string> itemName,
        Func<ItemEventKind, string> itemEventLabel,
        Func<OutReason, string> outReasonLabel,
        IReadOnlyList<PhoneReport>? reports = null,
        IReadOnlyList<GamePhaseEvent>? phases = null)
    {
        reports ??= [];
        phases ??= [];
        string TeamName(Guid? id) => teams.FirstOrDefault(t => t.Id == id)?.Name ?? "";
        var sheets = new List<TeamRetex>();
        foreach (var team in teams)
        {
            var own = messages.Where(m => m.IsFor(team)).OrderBy(m => m.SentAt).ToList();
            var outcomes = missions
                .Where(m => m.TeamIds.Contains(team.Id) && m.IsEnabled)
                .OrderBy(m => m.StartMinutes)
                .Select(m => new MissionOutcome(m, missionTime(m.StartMinutes), missionTime(m.EndMinutes),
                    own.FirstOrDefault(x => x.Kind == MessageKind.MissionAssigned && x.MissionId == m.Id)?.SentAt,
                    EndOf(own, m.Id),
                    team.CompletedMissionIds.Contains(m.Id)))
                .ToList();

            var trail = positions.Where(p => p.TeamId == team.Id).OrderBy(p => p.ReceivedAt).ToList();
            var distance = 0.0;
            for (var i = 1; i < trail.Count; i++)
            {
                var step = GeoMath.DistanceMeters(trail[i - 1].Point, trail[i].Point);
                if (step < MaxJumpMeters)
                    distance += step;
            }

            var events = new List<RetexEvent>();
            events.AddRange(own.Select(m => new RetexEvent(m.SentAt, team.Name, Category(m), m.Text)));
            events.AddRange(itemEvents.Where(e => e.TeamId == team.Id).Select(e =>
                new RetexEvent(e.At, team.Name, L.T("objet"), $"{itemEventLabel(e.Kind)} : {itemName(e.ItemId)}{(e.Notes.Length > 0 ? $" ({e.Notes})" : "")}")));
            var sent = reports.Where(r => !r.FromOrganizer && r.AuthorId == team.Id).OrderBy(r => r.SentAt).ToList();
            events.AddRange(sent.Select(r => new RetexEvent(r.SentAt, team.Name, ReportCategory(r), r.Text.Length > 0 ? r.Text : "📷")));
            events.AddRange(playerEvents.Where(e => e.TeamId == team.Id).Select(e =>
                new RetexEvent(e.At, team.Name, L.T("effectif"), e.IsOut
                    ? L.F("x_joueur_s_hors_jeu_x_x", e.Players, outReasonLabel(e.Reason), (e.Notes.Length > 0 ? $" ({e.Notes})" : ""))
                    : L.F("x_joueur_s_de_retour_en_jeu", e.Players))));

            sheets.Add(new TeamRetex(team, outcomes, distance / 1000, trail.Count, trail.FirstOrDefault()?.ReceivedAt, trail.LastOrDefault()?.ReceivedAt,
                playerEvents.Where(e => e.TeamId == team.Id && e.IsOut).Sum(e => e.Players), own, events.OrderBy(e => e.At).ToList())
            {
                Trail = trail.Select(p => p.Point).ToList(),
                Reports = sent,
            });
        }

        // Chronologie globale : messages (une seule fois, avec leurs destinataires), objets, effectifs.
        var timeline = new List<RetexEvent>();
        timeline.AddRange(messages.Select(m => new RetexEvent(m.SentAt, m.Target switch
        {
            MessageTarget.Team => TeamName(m.TargetId),
            MessageTarget.Faction => L.T("faction"),
            _ => L.T("toutes_les_equipes"),
        }, Category(m), m.Text)));
        timeline.AddRange(itemEvents.Select(e => new RetexEvent(e.At, TeamName(e.TeamId), L.T("objet"), $"{itemEventLabel(e.Kind)} : {itemName(e.ItemId)}")));
        timeline.AddRange(reports.Select(r => new RetexEvent(r.SentAt, r.FromOrganizer ? L.F("orga_x", r.Author) : r.Author,
            ReportCategory(r), r.Text.Length > 0 ? r.Text : "📷")));
        var previous = GamePhase.NotStarted;
        foreach (var phase in phases.OrderBy(p => p.At))
        {
            timeline.Add(new RetexEvent(phase.At, L.T("toutes_les_equipes"), L.T("partie"), GamePhases.AlertTitle(phase.Phase, previous)));
            previous = phase.Phase;
        }
        timeline.AddRange(playerEvents.Select(e => new RetexEvent(e.At, TeamName(e.TeamId), L.T("effectif"), e.IsOut
            ? L.F("x_joueur_s_hors_jeu_x", e.Players, outReasonLabel(e.Reason))
            : L.F("x_joueur_s_de_retour_en_jeu", e.Players))));
        return new OperationRetex(sheets, timeline.OrderBy(e => e.At).ToList());
    }

    private static string ReportCategory(PhoneReport report) =>
        report.Recipient == MessageSender.Hq ? L.T("message_au_qg") : L.T("message_a_l_orga");

    private static DateTimeOffset? EndOf(IReadOnlyList<OrgaMessage> messages, Guid missionId)
    {
        var ended = messages.FirstOrDefault(x => x.Kind == MessageKind.MissionEnded && x.MissionId == missionId)?.SentAt;
        if (ended is not null)
            return ended;
        // Mission terminée en diffusant la suivante : la fin est l'annonce suivante.
        var assigned = messages.FirstOrDefault(x => x.Kind == MessageKind.MissionAssigned && x.MissionId == missionId);
        return assigned is null ? null : messages.FirstOrDefault(x => x.Kind == MessageKind.MissionAssigned && x.SentAt > assigned.SentAt)?.SentAt;
    }

    private static string Category(OrgaMessage message) => message.Kind switch
    {
        MessageKind.MissionAssigned => L.T("mission_diffusee"),
        MessageKind.MissionEnded => L.T("mission_terminee"),
        _ => message.Sender == MessageSender.Hq ? L.T("message_qg") : L.T("message_orga"),
    };
}
