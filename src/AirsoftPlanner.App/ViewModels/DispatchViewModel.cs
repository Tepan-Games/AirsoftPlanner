using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Core.Geo;
using AirsoftPlanner.Core.Gps;
using AirsoftPlanner.Core.Planning;
using AirsoftPlanner.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AirsoftPlanner.Core.Localization;

namespace AirsoftPlanner.App.ViewModels;

/// <summary>Destinataire possible d'un message : toutes les équipes, une faction ou une équipe.</summary>
public record MessageTargetOption(MessageTarget Target, Guid? Id, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Message envoyé, avec les équipes qui l'ont reçu sur leur téléphone.</summary>
public partial class MessageRowViewModel(OrgaMessage message, string audience) : ViewModelBase
{
    public OrgaMessage Model => message;

    public string Time => message.SentAt.LocalDateTime.ToString("HH:mm");

    public string Audience => audience;

    /// <summary>« QG » (ordre en jeu) ou « Orga ».</summary>
    public string Sender => message.Sender == MessageSender.Hq ? "QG" : L.T("orga_2");

    public string Text => message.Text;

    public bool IsMission => message.Kind != MessageKind.Text;

    public bool HasPhoto => message.Photo is { Length: > 0 };

    private Avalonia.Media.Imaging.Bitmap? _thumbnail;

    /// <summary>Vignette de la photo jointe.</summary>
    public Avalonia.Media.Imaging.Bitmap? Thumbnail =>
        _thumbnail ??= HasPhoto ? Avalonia.Media.Imaging.Bitmap.DecodeToWidth(new System.IO.MemoryStream(message.Photo!), 160) : null;

    [ObservableProperty]
    private string _delivery = "";
}

/// <summary>Équipe de la mission sélectionnée sur la frise du suivi : état de la mission pour elle, actions possibles.</summary>
public record MissionTeamRow(TeamViewModel Team, string State, bool CanPublish, bool CanEnd);

/// <summary>Message reçu d'un téléphone (équipe ou orga).</summary>
public partial class ReportRowViewModel(PhoneReport report, string position) : ViewModelBase
{
    public PhoneReport Model => report;

    public string Time => report.SentAt.LocalDateTime.ToString("HH:mm");

    public string Author => report.FromOrganizer ? L.F("orga_x", report.Author) : report.Author;

    /// <summary>« → QG » ou « → Orga ».</summary>
    public string Recipient => report.Recipient == MessageSender.Hq ? "→ QG" : "→ Orga";

    public string Text => report.Text;

    public string Position => position;

    public bool HasPosition => position.Length > 0;

    public bool HasPhoto => report.Photo is { Length: > 0 };

    private Avalonia.Media.Imaging.Bitmap? _thumbnail;

    public Avalonia.Media.Imaging.Bitmap? Thumbnail =>
        _thumbnail ??= HasPhoto ? Avalonia.Media.Imaging.Bitmap.DecodeToWidth(new System.IO.MemoryStream(report.Photo!), 160) : null;

    public bool IsRead
    {
        get => report.IsRead;
        set => SetProperty(report.IsRead, value, report, (r, v) => r.IsRead = v);
    }
}

/// <summary>Question posée à l'orga : terminer une mission, en diffuser une nouvelle.</summary>
/// <param name="current">Mission en cours (son résultat se saisit dans la question), ou null.</param>
/// <param name="row">Ligne de l'équipe : choix d'annoncer ou non le résultat.</param>
public class DiffusionPromptViewModel(TeamViewModel team, DiffusionSuggestion suggestion, string question, MissionViewModel? current,
    TeamDispatchViewModel? row) : ViewModelBase
{
    public TeamViewModel Team => team;

    public MissionViewModel? Current => current;

    public TeamDispatchViewModel? Row => row;

    public bool HasCurrent => current is not null;

    public DiffusionSuggestion Suggestion => suggestion;

    public string Question => question;

    /// <summary>« Diffuser » : terminer la mission en cours (s'il y en a une) et diffuser la suivante.</summary>
    public string AcceptLabel => suggestion.Next is null ? L.T("terminer") : suggestion.Current is null ? L.T("diffuser") : L.T("terminer_et_diffuser");

    public bool CanEndOnly => suggestion.Current is not null && suggestion.Next is not null;
}

/// <summary>Diffusion des missions d'une équipe (tableau de toutes les équipes).</summary>
public partial class TeamDispatchViewModel(TeamViewModel team) : ViewModelBase
{
    public TeamViewModel Team => team;

    /// <summary>Mission diffusée (son résultat peut être saisi avant de la terminer).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCurrent))]
    private MissionViewModel? _current;

    public bool HasCurrent => Current is not null;

    /// <summary>Le message de fin de mission annonce son résultat à l'équipe (choix de l'orga).</summary>
    [ObservableProperty]
    private bool _announceResult = true;

    [ObservableProperty]
    private string _published = "";

    [ObservableProperty]
    private IReadOnlyList<MissionViewModel> _missions = [];

    [ObservableProperty]
    private MissionViewModel? _selectedMission;
}

/// <summary>
/// Messages de l'orga vers les téléphones et diffusion des missions. Rien n'est envoyé automatiquement :
/// le logiciel pose la question au bon moment (fin d'une mission, approche de la suivante), l'orga décide.
/// </summary>
public partial class DispatchViewModel : ViewModelBase
{
    private readonly OperationFile _file;
    private readonly OperationViewModel _operation;
    private readonly TeamsViewModel _teams;
    private readonly FactionsViewModel _factions;
    private readonly MissionsViewModel _missions;
    private readonly TerrainViewModel _terrain;
    private readonly GameItemsViewModel _items;
    private readonly List<OrgaMessage> _messages;
    private readonly List<GamePhaseEvent> _phaseEvents;

    // Équipes ayant reçu chaque message (téléphone passé depuis l'envoi) : suivi de la session en cours.
    private readonly Dictionary<Guid, HashSet<Guid>> _delivered = [];

    // « Plus tard » : la question revient après ce délai.
    private readonly Dictionary<(Guid Team, Guid? Current, Guid? Next), double> _snoozed = [];

    // Missions urgentes à proposer tout de suite (même si une mission est en cours).
    private readonly Dictionary<Guid, Guid> _urgent = [];
    private double _now;

    private readonly Services.IFileDialogService? _dialogs;

    public DispatchViewModel(OperationFile file, OperationViewModel operation, TeamsViewModel teams, FactionsViewModel factions,
        MissionsViewModel missions, TerrainViewModel terrain, GameItemsViewModel items, Services.IFileDialogService? dialogs = null)
    {
        _dialogs = dialogs;
        _file = file;
        _operation = operation;
        _teams = teams;
        _factions = factions;
        _missions = missions;
        _terrain = terrain;
        _items = items;
        _messages = file.LoadMessages().ToList();
        _phaseEvents = file.LoadGamePhaseEvents().ToList();
        foreach (var report in file.LoadPhoneReports().OrderByDescending(r => r.ReceivedAt))
            Reports.Add(new ReportRowViewModel(report, PositionOf(report)));
        RefreshUnread();
        missions.ResultsChanged += () => Refresh(_now);
        missions.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MissionsViewModel.Selected))
                RefreshSelectedMission();
        };
        teams.Items.CollectionChanged += (_, _) => RefreshTargets();
        factions.Items.CollectionChanged += (_, _) => RefreshTargets();
        RefreshTargets();
        RefreshMessages();
    }

    // ----- Phase de la partie : début, pause, reprise, fin (alerte particulière sur les téléphones) -----

    public GamePhase Phase => _file.Operation.GamePhase;

    /// <summary>« ⏸ Jeu en pause depuis 14:32 ».</summary>
    public string PhaseText => GamePhases.Symbol(Phase) + " " + GamePhases.Label(Phase)
        + (_file.Operation.GamePhaseSince is { } since ? L.F("depuis_x", since.LocalDateTime.ToString("HH:mm")) : "");

    public bool CanStartGame => Phase is GamePhase.NotStarted or GamePhase.Ended;

    public bool CanPauseGame => Phase == GamePhase.Running;

    public bool CanResumeGame => Phase == GamePhase.Paused;

    public bool CanEndGame => Phase is GamePhase.Running or GamePhase.Paused;

    /// <summary>Historique des phases (RETEX).</summary>
    public IReadOnlyList<GamePhaseEvent> PhaseEvents => _phaseEvents;

    [RelayCommand]
    private async System.Threading.Tasks.Task StartGameAsync()
    {
        if (CanStartGame && await Confirm(L.T("debut_de_partie"), L.T("annoncer_le_debut_de_partie_a_tous_les_telephone"), L.T("debut_de_partie")))
            SetPhase(GamePhase.Running);
    }

    [RelayCommand]
    private void PauseGame()
    {
        if (CanPauseGame)
            SetPhase(GamePhase.Paused);
    }

    [RelayCommand]
    private void ResumeGame()
    {
        if (CanResumeGame)
            SetPhase(GamePhase.Running);
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task EndGameAsync()
    {
        if (CanEndGame && await Confirm(L.T("fin_de_partie"), L.T("annoncer_la_fin_de_partie_a_tous_les_telephones"), L.T("fin_de_partie")))
            SetPhase(GamePhase.Ended);
    }

    private System.Threading.Tasks.Task<bool> Confirm(string title, string message, string confirm) =>
        _dialogs?.ConfirmAsync(title, message, confirm) ?? System.Threading.Tasks.Task.FromResult(true);

    /// <summary>Change la phase : enregistrée, envoyée aux téléphones à leur prochain échange (alerte particulière).</summary>
    public void SetPhase(GamePhase phase)
    {
        var now = DateTimeOffset.Now;
        _file.Operation.GamePhase = phase;
        _file.Operation.GamePhaseSince = now;
        var change = new GamePhaseEvent { Phase = phase, At = now };
        _file.Add(change);
        _phaseEvents.Add(change);
        OnPropertyChanged(nameof(Phase));
        OnPropertyChanged(nameof(PhaseText));
        OnPropertyChanged(nameof(CanStartGame));
        OnPropertyChanged(nameof(CanPauseGame));
        OnPropertyChanged(nameof(CanResumeGame));
        OnPropertyChanged(nameof(CanEndGame));
    }

    // ----- Messages reçus des téléphones -----

    /// <summary>Messages des téléphones vers l'orga, le plus récent en premier.</summary>
    public ObservableCollection<ReportRowViewModel> Reports { get; } = [];

    /// <summary>Messages reçus pas encore lus : bandeau du suivi.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnreadReports))]
    private int _unreadReports;

    public bool HasUnreadReports => UnreadReports > 0;

    [ObservableProperty]
    private string _unreadSummary = "";

    public bool HasReport(Guid clientId) => Reports.Any(r => r.Model.ClientId == clientId);

    public void AddReport(PhoneReport report)
    {
        _file.Add(report);
        Reports.Insert(0, new ReportRowViewModel(report, PositionOf(report)));
        RefreshUnread();
    }

    /// <summary>Tous les messages reçus (RETEX).</summary>
    public IReadOnlyList<PhoneReport> AllReports => Reports.Select(r => r.Model).ToList();

    [RelayCommand]
    private void MarkReportsRead()
    {
        foreach (var row in Reports.Where(r => !r.IsRead))
            row.IsRead = true;
        RefreshUnread();
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task ShowReportPhotoAsync(ReportRowViewModel? row)
    {
        if (row?.Model.Photo is { Length: > 0 } photo && _dialogs is not null)
            await _dialogs.ShowImageAsync(row.Author, $"{row.Time} — {row.Text}", photo);
    }

    private void RefreshUnread()
    {
        var unread = Reports.Where(r => !r.IsRead).ToList();
        UnreadReports = unread.Count;
        UnreadSummary = unread.Count == 0 ? ""
            : L.F("x_message_s_recu_s_des_telephones_x", unread.Count,
                $"{unread[0].Author} {unread[0].Recipient} ({unread[0].Time}) : {(unread[0].Text.Length > 0 ? unread[0].Text : "📷")}");
    }

    private string PositionOf(PhoneReport report) => report.Latitude is { } lat && report.Longitude is { } lon
        ? Coordinates.Format(new GeoPoint(lat, lon), _operation.CoordinateFormat)
        : "";

    // ----- Messages -----

    /// <summary>Tous les messages envoyés (RETEX).</summary>
    public IReadOnlyList<OrgaMessage> AllMessages => _messages;

    public ObservableCollection<MessageTargetOption> Targets { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private MessageTargetOption? _selectedTarget;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _composeText = "";

    /// <summary>Photo à joindre au prochain message (JPEG réduit).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasComposePhoto))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private byte[]? _composePhoto;

    public bool HasComposePhoto => ComposePhoto is not null;

    [RelayCommand]
    private async System.Threading.Tasks.Task AttachPhotoAsync()
    {
        if (_dialogs is null || await _dialogs.PickPhotoFileAsync() is not { } path)
            return;
        try
        {
            ComposePhoto = Services.PhotoResizer.ToJpeg(await System.IO.File.ReadAllBytesAsync(path));
        }
        catch (Exception ex) when (ex is System.IO.IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            await _dialogs.ShowErrorAsync(L.F("photo_illisible_x", ex.Message));
        }
    }

    [RelayCommand]
    private void RemovePhoto() => ComposePhoto = null;

    /// <summary>Téléphone d'orga : les derniers messages envoyés aux équipes (pour suivre ce qui a été transmis).</summary>
    public IReadOnlyList<PhoneMessage> PhoneMessagesForOrga() => _messages
        .OrderBy(m => m.SentAt)
        .TakeLast(50)
        .Select(m => new PhoneMessage(m.Id, m.SentAt, m.Text, AudienceOf(m), m.Kind, MessageSender.Orga, "", m.Photo is { Length: > 0 }))
        .ToList();

    /// <summary>Photo d'un message, si ce message est destiné à l'équipe.</summary>
    public byte[]? PhotoFor(TeamViewModel team, Guid messageId) =>
        _messages.FirstOrDefault(m => m.Id == messageId && m.IsFor(team.Model))?.Photo;

    /// <summary>Message du QG (ordre en jeu, roleplay) ; sinon message de l'orga (organisation, sécurité).</summary>
    [ObservableProperty]
    private bool _composeAsHq = true;

    /// <summary>Messages envoyés, du plus récent au plus ancien.</summary>
    public ObservableCollection<MessageRowViewModel> Messages { get; } = [];

    [RelayCommand(CanExecute = nameof(CanSend))]
    private void Send()
    {
        AddMessage(ComposeText.Trim(), SelectedTarget!.Target, SelectedTarget.Id, MessageKind.Text, null,
            ComposeAsHq ? MessageSender.Hq : MessageSender.Orga, ComposePhoto);
        ComposeText = "";
        ComposePhoto = null;
    }

    private bool CanSend => SelectedTarget is not null && (ComposeText.Trim().Length > 0 || ComposePhoto is not null);

    /// <summary>Messages destinés à une équipe (les plus récents), pour son téléphone ; ils sont alors comptés comme reçus.</summary>
    public IReadOnlyList<PhoneMessage> PhoneMessagesFor(TeamViewModel team)
    {
        // Mission en cours au moment de chaque message (regroupement sur le téléphone), puis les plus récents.
        var tagged = MessageHistory.TagMissions(_messages.Where(m => m.IsFor(team.Model)),
                id => _missions.Missions.FirstOrDefault(m => m.Model.Id == id)?.Name ?? L.T("mission_supprimee"))
            .TakeLast(100)
            .ToList();
        var messages = tagged.Select(t => t.Message).ToList();
        var changed = false;
        foreach (var message in messages)
        {
            if (!_delivered.TryGetValue(message.Id, out var teams))
                _delivered[message.Id] = teams = [];
            changed |= teams.Add(team.Model.Id);
        }

        if (changed)
            RefreshDelivery();
        return tagged.Select(t => new PhoneMessage(t.Message.Id, t.Message.SentAt, t.Message.Text, AudienceOf(t.Message), t.Message.Kind,
            t.Message.Sender, t.Mission, t.Message.Photo is { Length: > 0 })).ToList();
    }

    // ----- Missions -----

    /// <summary>Questions en attente d'une décision de l'orga.</summary>
    public ObservableCollection<DiffusionPromptViewModel> Prompts { get; } = [];

    [ObservableProperty]
    private bool _hasPrompts;

    /// <summary>Toutes les équipes : mission diffusée, choix d'une autre mission.</summary>
    public ObservableCollection<TeamDispatchViewModel> Teams { get; } = [];

    [RelayCommand]
    private void Accept(DiffusionPromptViewModel? prompt)
    {
        if (prompt is null)
            return;
        _urgent.Remove(prompt.Team.Model.Id);
        if (prompt.Suggestion.Next is { } next)
            Publish(prompt.Team, next, prompt.Suggestion.Current);
        else
            End(prompt.Team);
    }

    [RelayCommand]
    private void EndOnly(DiffusionPromptViewModel? prompt)
    {
        if (prompt is not null)
            End(prompt.Team);
    }

    [RelayCommand]
    private void Later(DiffusionPromptViewModel? prompt)
    {
        if (prompt is null)
            return;
        _urgent.Remove(prompt.Team.Model.Id);
        var s = prompt.Suggestion;
        _snoozed[(s.TeamId, s.Current?.Id, s.Next?.Id)] = _now + 10;
        Refresh(_now);
    }

    [RelayCommand]
    private void PublishSelected(TeamDispatchViewModel? row)
    {
        if (row?.SelectedMission is { } mission)
            Publish(row.Team, mission.Model, CurrentOf(row.Team));
    }

    [RelayCommand]
    private void EndPublished(TeamDispatchViewModel? row)
    {
        if (row is not null && row.Team.Model.PublishedMissionId is not null)
            End(row.Team);
    }

    /// <summary>Mission urgente créée pendant l'OP : la question de sa diffusion est posée tout de suite.</summary>
    public void ProposeUrgent(TeamViewModel team, Mission mission)
    {
        _urgent[team.Model.Id] = mission.Id;
        Refresh(_now);
    }

    /// <summary>Diffuse une mission à l'équipe (la mission précédente est terminée) et la prévient par message.</summary>
    public void Publish(TeamViewModel team, Mission mission, Mission? previous)
    {
        var model = team.Model;
        if (previous is not null && previous.Id != mission.Id && !model.CompletedMissionIds.Contains(previous.Id))
            model.CompletedMissionIds = [.. model.CompletedMissionIds, previous.Id];
        model.PublishedMissionId = mission.Id;
        var zone = _terrain.Zones.FirstOrDefault(z => z.Model.Id == mission.ZoneId)?.Name;
        var text = (previous is not null && previous.Id != mission.Id
                       ? Announced(team, previous) is { } result ? L.F("mission_x_terminee_x", previous.Name, result) : L.F("mission_x_terminee", previous.Name)
                       : "")
                   + L.F("nouvelle_mission_x_x_x", mission.Name, MissionTime.Format(mission.StartMinutes), MissionTime.Format(mission.EndMinutes))
                   + (zone is null ? ")" : $", {zone})");
        AddMessage(text, MessageTarget.Team, model.Id, MessageKind.MissionAssigned, mission.Id, MessageSender.Hq);
    }

    /// <summary>Termine la mission diffusée : l'équipe attend les ordres.</summary>
    public void End(TeamViewModel team)
    {
        var model = team.Model;
        if (CurrentOf(team) is not { } current)
            return;
        if (!model.CompletedMissionIds.Contains(current.Id))
            model.CompletedMissionIds = [.. model.CompletedMissionIds, current.Id];
        model.PublishedMissionId = null;
        var text = Announced(team, current) is { } result
            ? L.F("mission_x_terminee_x_attendez_les_ordres", current.Name, result)
            : L.F("mission_x_terminee_attendez_les_ordres", current.Name);
        AddMessage(text, MessageTarget.Team, model.Id, MessageKind.MissionEnded, current.Id, MessageSender.Hq);
    }

    /// <summary>Résultat à annoncer dans le message de fin (« réussie »), si l'orga l'a saisi et choisi de l'annoncer.</summary>
    private string? Announced(TeamViewModel team, Mission mission) =>
        mission.Result != MissionResult.NotEvaluated && Teams.FirstOrDefault(r => r.Team == team) is not { AnnounceResult: false }
            ? MissionResults.Label(mission.Result).ToLower(L.Culture)
            : null;

    // ----- Mission sélectionnée sur la frise du suivi -----

    /// <summary>Équipes de la mission sélectionnée, avec ce qu'on peut faire pour chacune (diffuser, terminer).</summary>
    public ObservableCollection<MissionTeamRow> SelectedMissionTeams { get; } = [];

    /// <summary>Le résultat se saisit une fois la mission commencée (ou diffusée).</summary>
    [ObservableProperty]
    private bool _showSelectedResult;

    [RelayCommand]
    private void PublishMissionTo(MissionTeamRow? row)
    {
        if (row is { CanPublish: true } && _missions.Selected is { } mission)
            Publish(row.Team, mission.Model, CurrentOf(row.Team));
    }

    [RelayCommand]
    private void EndMissionFor(MissionTeamRow? row)
    {
        if (row is { CanEnd: true })
            End(row.Team);
    }

    private void RefreshSelectedMission()
    {
        var mission = _missions.Selected?.Model;
        var rows = mission is null ? [] : _missions.Columns.Where(t => mission.TeamIds.Contains(t.Model.Id)).Select(team =>
        {
            var published = team.Model.PublishedMissionId == mission.Id;
            var done = team.Model.CompletedMissionIds.Contains(mission.Id);
            var state = published ? L.T("etat_diffusee")
                : done ? L.T("etat_terminee")
                : _now >= mission.StartMinutes ? L.F("etat_prevue_depuis_x", MissionTime.Format(mission.StartMinutes))
                : L.F("etat_a_venir_x", MissionTime.Format(mission.StartMinutes));
            return new MissionTeamRow(team, state, !published && mission.IsEnabled, published);
        }).ToList();
        if (!rows.SequenceEqual(SelectedMissionTeams))
        {
            SelectedMissionTeams.Clear();
            foreach (var row in rows)
                SelectedMissionTeams.Add(row);
        }
        ShowSelectedResult = mission is not null && (_now >= mission.StartMinutes || mission.Result != MissionResult.NotEvaluated
            || rows.Any(r => r.CanEnd || r.State == L.T("etat_terminee")));
    }

    // ----- Score -----

    /// <summary>Points des factions d'après le résultat des missions (une fois par mission et par faction).</summary>
    public ObservableCollection<ScoreLine> FactionScores { get; } = [];

    /// <summary>Points des équipes (chaque équipe engagée reçoit les points de la mission).</summary>
    public ObservableCollection<ScoreLine> TeamScores { get; } = [];

    [ObservableProperty]
    private string _scoreSummary = "";

    private void RefreshScores()
    {
        var missions = _missions.Missions.Select(m => m.Model).ToList();
        var teams = _teams.Items.Where(t => t.IsPlaying).Select(t => t.Model).ToList();
        Replace(FactionScores, Scoreboard.ByFaction(missions, _factions.Items.Select(f => f.Model), teams));
        Replace(TeamScores, Scoreboard.ByTeam(missions, teams, t => _factions.Items.FirstOrDefault(f => f.Model.Id == t.FactionId)?.Color ?? "#607D8B"));
        var evaluated = missions.Count(m => m.IsEnabled && m.Result != MissionResult.NotEvaluated);
        ScoreSummary = L.F("x_mission_s_evaluee_s_sur_x", evaluated, missions.Count(m => m.IsEnabled));
    }

    private static void Replace(ObservableCollection<ScoreLine> target, IReadOnlyList<ScoreLine> lines)
    {
        if (target.SequenceEqual(lines))
            return;
        target.Clear();
        foreach (var line in lines)
            target.Add(line);
    }

    /// <summary>Mission diffusée à une équipe, pour son téléphone.</summary>
    public MissionBrief? MissionBriefFor(TeamViewModel team, CoordinateFormat format)
    {
        if (CurrentOf(team) is not { } mission)
            return null;

        var zone = _terrain.Zones.FirstOrDefault(z => z.Model.Id == mission.ZoneId);
        var center = zone is { Points.Count: > 0 } ? GeoMath.Centroid(zone.Points) : (GeoPoint?)null;
        var items = string.Join(", ", mission.Items.Select(u => _items.Items.FirstOrDefault(i => i.Model.Id == u.ItemId) is { } item ? $"{u.Quantity} × {item.Name}" : null).OfType<string>());
        return new MissionBrief(
            mission.Name,
            true,
            new DateTimeOffset(_operation.ToDateTime(mission.StartMinutes)),
            new DateTimeOffset(_operation.ToDateTime(mission.EndMinutes)),
            zone?.Name ?? "",
            center is { } c ? Coordinates.Format(c, format) : "",
            center?.Latitude,
            center?.Longitude,
            mission.Description,
            items,
            mission.Id);
    }

    /// <summary>Recalcule les questions à poser (appelé à chaque rafraîchissement du suivi).</summary>
    public void Refresh(double nowMinutes)
    {
        _now = nowMinutes;
        var missions = _missions.Missions.Select(m => m.Model).ToList();
        var prompts = new List<DiffusionPromptViewModel>();
        foreach (var team in _missions.Columns)
        {
            var model = team.Model;
            DiffusionSuggestion? suggestion = null;
            if (_urgent.TryGetValue(model.Id, out var urgentId) && missions.FirstOrDefault(m => m.Id == urgentId) is { } urgent
                && model.PublishedMissionId != urgentId)
                suggestion = new DiffusionSuggestion(model.Id, DiffusionAdvice.SendNext, CurrentOf(team), urgent);
            suggestion ??= DiffusionAdvisor.Advise(model.Id, missions, model.PublishedMissionId, model.CompletedMissionIds, nowMinutes);
            if (suggestion is null)
                continue;
            if (_snoozed.TryGetValue((model.Id, suggestion.Current?.Id, suggestion.Next?.Id), out var until) && nowMinutes < until)
                continue;
            var current = suggestion.Current is { } c ? _missions.Missions.FirstOrDefault(m => m.Model == c) : null;
            prompts.Add(new DiffusionPromptViewModel(team, suggestion, Question(team, suggestion, nowMinutes), current,
                Teams.FirstOrDefault(r => r.Team == team)));
        }

        // Liste remplacée seulement si elle change : les boutons ne clignotent pas à chaque rafraîchissement.
        if (!prompts.Select(p => p.Question).SequenceEqual(Prompts.Select(p => p.Question)))
        {
            Prompts.Clear();
            foreach (var prompt in prompts)
                Prompts.Add(prompt);
        }

        HasPrompts = Prompts.Count > 0;
        RefreshTeams();
        RefreshScores();
        RefreshSelectedMission();
    }

    // ----- Interne -----

    private Mission? CurrentOf(TeamViewModel team) => team.Model.PublishedMissionId is { } id
        ? _missions.Missions.FirstOrDefault(m => m.Model.Id == id)?.Model
        : null;

    private string Question(TeamViewModel team, DiffusionSuggestion s, double now)
    {
        string When(Mission m) => m.StartMinutes > now ? L.F("debut_x", MissionTime.Format(m.StartMinutes)) : L.F("prevue_depuis_x", MissionTime.Format(m.StartMinutes));
        if (s.Current is { } running && s.Next is { } urgent && _urgent.ContainsKey(team.Model.Id))
            return L.F("x_mission_urgente_x_la_diffuser_maintenant_x_ser", team.Name, urgent.Name, running.Name);
        return (s.Current, s.Next) switch
        {
            ({ } current, { } next) => L.F("x_x_devait_finir_a_x_terminer_et_diffuser_x_x", team.Name, current.Name, MissionTime.Format(current.EndMinutes), next.Name, When(next)),
            ({ } current, null) => L.F("x_x_devait_finir_a_x_derniere_mission_la_termine", team.Name, current.Name, MissionTime.Format(current.EndMinutes)),
            (null, { } next) => L.F("x_diffuser_la_mission_x_x", team.Name, next.Name, When(next)),
            _ => "",
        };
    }

    private void AddMessage(string text, MessageTarget target, Guid? targetId, MessageKind kind, Guid? missionId, MessageSender sender,
        byte[]? photo = null)
    {
        var message = new OrgaMessage
        {
            Text = text,
            Target = target,
            TargetId = targetId,
            Kind = kind,
            MissionId = missionId,
            Sender = sender,
            Photo = photo,
            SentAt = DateTimeOffset.Now,
        };
        _file.Add(message);
        _messages.Add(message);
        RefreshMessages();
        Refresh(_now);
    }

    private string AudienceOf(OrgaMessage message) => message.Target switch
    {
        MessageTarget.Faction => L.F("faction_x", _factions.Items.FirstOrDefault(f => f.Model.Id == message.TargetId)?.Name ?? "?"),
        MessageTarget.Team => _teams.Items.FirstOrDefault(t => t.Model.Id == message.TargetId)?.Name ?? L.T("equipe_supprimee"),
        _ => L.T("toutes_les_equipes"),
    };

    private void RefreshTargets()
    {
        var selected = SelectedTarget;
        Targets.Clear();
        Targets.Add(new MessageTargetOption(MessageTarget.AllTeams, null, L.T("toutes_les_equipes")));
        foreach (var faction in _factions.Items)
            Targets.Add(new MessageTargetOption(MessageTarget.Faction, faction.Model.Id, L.F("faction_x", faction.Name)));
        foreach (var team in _teams.Items)
            Targets.Add(new MessageTargetOption(MessageTarget.Team, team.Model.Id, L.F("equipe_x", team.Name)));
        SelectedTarget = Targets.FirstOrDefault(t => selected is not null && t.Target == selected.Target && t.Id == selected.Id) ?? Targets[0];
    }

    private void RefreshMessages()
    {
        Messages.Clear();
        foreach (var message in _messages.OrderByDescending(m => m.SentAt))
            Messages.Add(new MessageRowViewModel(message, AudienceOf(message)));
        RefreshDelivery();
    }

    private void RefreshDelivery()
    {
        foreach (var row in Messages)
        {
            var targeted = _teams.Items.Where(t => row.Model.IsFor(t.Model)).ToList();
            var received = _delivered.GetValueOrDefault(row.Model.Id) ?? [];
            var waiting = targeted.Where(t => !received.Contains(t.Model.Id)).Select(t => t.Name).ToList();
            row.Delivery = targeted.Count == 0 ? L.T("aucune_equipe")
                : waiting.Count == 0 ? L.T("recu_par_toutes_les_equipes_visees")
                : waiting.Count == targeted.Count ? L.T("en_attente_aucun_telephone_n_a_encore_recu")
                : L.F("recu_par_x_x_en_attente_x_x", targeted.Count - waiting.Count, targeted.Count, string.Join(", ", waiting.Take(6)), (waiting.Count > 6 ? "…" : ""));
        }
    }

    private void RefreshTeams()
    {
        if (!Teams.Select(t => t.Team).SequenceEqual(_missions.Columns))
        {
            Teams.Clear();
            foreach (var team in _missions.Columns)
                Teams.Add(new TeamDispatchViewModel(team));
        }

        foreach (var row in Teams)
        {
            var current = CurrentOf(row.Team);
            row.Current = current is null ? null : _missions.Missions.FirstOrDefault(m => m.Model == current);
            row.Published = current is null ? L.T("aucune_mission_diffusee")
                : $"{current.Name} ({MissionTime.Format(current.StartMinutes)}–{MissionTime.Format(current.EndMinutes)})";
            var missions = _missions.Missions
                .Where(m => m.Model.IsEnabled && m.Model.TeamIds.Contains(row.Team.Model.Id))
                .OrderBy(m => m.Model.StartMinutes)
                .ToList();
            if (!missions.SequenceEqual(row.Missions))
                row.Missions = missions;
            if (row.SelectedMission is null || !missions.Contains(row.SelectedMission))
                row.SelectedMission = missions.FirstOrDefault(m => !row.Team.Model.CompletedMissionIds.Contains(m.Model.Id) && m.Model != current)
                                      ?? missions.FirstOrDefault();
        }
    }
}
