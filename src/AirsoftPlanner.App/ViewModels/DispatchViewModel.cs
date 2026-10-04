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
    public string Sender => message.Sender == MessageSender.Hq ? "QG" : "Orga";

    public string Text => message.Text;

    public bool IsMission => message.Kind != MessageKind.Text;

    [ObservableProperty]
    private string _delivery = "";
}

/// <summary>Question posée à l'orga : terminer une mission, en diffuser une nouvelle.</summary>
public class DiffusionPromptViewModel(TeamViewModel team, DiffusionSuggestion suggestion, string question) : ViewModelBase
{
    public TeamViewModel Team => team;

    public DiffusionSuggestion Suggestion => suggestion;

    public string Question => question;

    /// <summary>« Diffuser » : terminer la mission en cours (s'il y en a une) et diffuser la suivante.</summary>
    public string AcceptLabel => suggestion.Next is null ? "Terminer" : suggestion.Current is null ? "Diffuser" : "Terminer et diffuser";

    public bool CanEndOnly => suggestion.Current is not null && suggestion.Next is not null;
}

/// <summary>Diffusion des missions d'une équipe (tableau de toutes les équipes).</summary>
public partial class TeamDispatchViewModel(TeamViewModel team) : ViewModelBase
{
    public TeamViewModel Team => team;

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

    // Équipes ayant reçu chaque message (téléphone passé depuis l'envoi) : suivi de la session en cours.
    private readonly Dictionary<Guid, HashSet<Guid>> _delivered = [];

    // « Plus tard » : la question revient après ce délai.
    private readonly Dictionary<(Guid Team, Guid? Current, Guid? Next), double> _snoozed = [];

    // Missions urgentes à proposer tout de suite (même si une mission est en cours).
    private readonly Dictionary<Guid, Guid> _urgent = [];
    private double _now;

    public DispatchViewModel(OperationFile file, OperationViewModel operation, TeamsViewModel teams, FactionsViewModel factions,
        MissionsViewModel missions, TerrainViewModel terrain, GameItemsViewModel items)
    {
        _file = file;
        _operation = operation;
        _teams = teams;
        _factions = factions;
        _missions = missions;
        _terrain = terrain;
        _items = items;
        _messages = file.LoadMessages().ToList();
        teams.Items.CollectionChanged += (_, _) => RefreshTargets();
        factions.Items.CollectionChanged += (_, _) => RefreshTargets();
        RefreshTargets();
        RefreshMessages();
    }

    // ----- Messages -----

    public ObservableCollection<MessageTargetOption> Targets { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private MessageTargetOption? _selectedTarget;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _composeText = "";

    /// <summary>Message du QG (ordre en jeu, roleplay) ; sinon message de l'orga (organisation, sécurité).</summary>
    [ObservableProperty]
    private bool _composeAsHq = true;

    /// <summary>Messages envoyés, du plus récent au plus ancien.</summary>
    public ObservableCollection<MessageRowViewModel> Messages { get; } = [];

    [RelayCommand(CanExecute = nameof(CanSend))]
    private void Send()
    {
        AddMessage(ComposeText.Trim(), SelectedTarget!.Target, SelectedTarget.Id, MessageKind.Text, null,
            ComposeAsHq ? MessageSender.Hq : MessageSender.Orga);
        ComposeText = "";
    }

    private bool CanSend => SelectedTarget is not null && ComposeText.Trim().Length > 0;

    /// <summary>Messages destinés à une équipe (les plus récents), pour son téléphone ; ils sont alors comptés comme reçus.</summary>
    public IReadOnlyList<PhoneMessage> PhoneMessagesFor(TeamViewModel team)
    {
        // Mission en cours au moment de chaque message (regroupement sur le téléphone), puis les plus récents.
        var tagged = MessageHistory.TagMissions(_messages.Where(m => m.IsFor(team.Model)),
                id => _missions.Missions.FirstOrDefault(m => m.Model.Id == id)?.Name ?? "Mission supprimée")
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
            t.Message.Sender, t.Mission)).ToList();
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
        var text = (previous is not null && previous.Id != mission.Id ? $"Mission « {previous.Name} » terminée. " : "")
                   + $"Nouvelle mission : {mission.Name} ({MissionTime.Format(mission.StartMinutes)}–{MissionTime.Format(mission.EndMinutes)}"
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
        AddMessage($"Mission « {current.Name} » terminée. Attendez les ordres.", MessageTarget.Team, model.Id,
            MessageKind.MissionEnded, current.Id, MessageSender.Hq);
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
            prompts.Add(new DiffusionPromptViewModel(team, suggestion, Question(team, suggestion, nowMinutes)));
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
    }

    // ----- Interne -----

    private Mission? CurrentOf(TeamViewModel team) => team.Model.PublishedMissionId is { } id
        ? _missions.Missions.FirstOrDefault(m => m.Model.Id == id)?.Model
        : null;

    private string Question(TeamViewModel team, DiffusionSuggestion s, double now)
    {
        string When(Mission m) => m.StartMinutes > now ? $"début {MissionTime.Format(m.StartMinutes)}" : $"prévue depuis {MissionTime.Format(m.StartMinutes)}";
        if (s.Current is { } running && s.Next is { } urgent && _urgent.ContainsKey(team.Model.Id))
            return $"{team.Name} : mission urgente « {urgent.Name} » — la diffuser maintenant (« {running.Name} » sera terminée) ?";
        return (s.Current, s.Next) switch
        {
            ({ } current, { } next) => $"{team.Name} : « {current.Name} » devait finir à {MissionTime.Format(current.EndMinutes)}. Terminer et diffuser « {next.Name} » ({When(next)}) ?",
            ({ } current, null) => $"{team.Name} : « {current.Name} » devait finir à {MissionTime.Format(current.EndMinutes)} (dernière mission). La terminer ?",
            (null, { } next) => $"{team.Name} : diffuser la mission « {next.Name} » ({When(next)}) ?",
            _ => "",
        };
    }

    private void AddMessage(string text, MessageTarget target, Guid? targetId, MessageKind kind, Guid? missionId, MessageSender sender)
    {
        var message = new OrgaMessage
        {
            Text = text,
            Target = target,
            TargetId = targetId,
            Kind = kind,
            MissionId = missionId,
            Sender = sender,
            SentAt = DateTimeOffset.Now,
        };
        _file.Add(message);
        _messages.Add(message);
        RefreshMessages();
        Refresh(_now);
    }

    private string AudienceOf(OrgaMessage message) => message.Target switch
    {
        MessageTarget.Faction => $"Faction {_factions.Items.FirstOrDefault(f => f.Model.Id == message.TargetId)?.Name ?? "?"}",
        MessageTarget.Team => _teams.Items.FirstOrDefault(t => t.Model.Id == message.TargetId)?.Name ?? "Équipe supprimée",
        _ => "Toutes les équipes",
    };

    private void RefreshTargets()
    {
        var selected = SelectedTarget;
        Targets.Clear();
        Targets.Add(new MessageTargetOption(MessageTarget.AllTeams, null, "Toutes les équipes"));
        foreach (var faction in _factions.Items)
            Targets.Add(new MessageTargetOption(MessageTarget.Faction, faction.Model.Id, $"Faction {faction.Name}"));
        foreach (var team in _teams.Items)
            Targets.Add(new MessageTargetOption(MessageTarget.Team, team.Model.Id, $"Équipe {team.Name}"));
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
            row.Delivery = targeted.Count == 0 ? "aucune équipe"
                : waiting.Count == 0 ? "✔ reçu par toutes les équipes visées"
                : waiting.Count == targeted.Count ? "en attente (aucun téléphone n'a encore reçu)"
                : $"reçu par {targeted.Count - waiting.Count}/{targeted.Count} · en attente : {string.Join(", ", waiting.Take(6))}{(waiting.Count > 6 ? "…" : "")}";
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
            row.Published = current is null ? "aucune mission diffusée"
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
