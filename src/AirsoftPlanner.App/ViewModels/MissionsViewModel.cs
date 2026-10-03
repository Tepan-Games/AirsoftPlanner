using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Core.Planning;
using AirsoftPlanner.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AirsoftPlanner.App.ViewModels;

/// <summary>Scénario : missions, frise par équipe et détection des incohérences du planning.</summary>
public partial class MissionsViewModel : ViewModelBase
{
    private const int DefaultDuration = 30;

    private readonly OperationFile _file;
    private readonly OperationViewModel _operation;
    private readonly TeamsViewModel _teams;
    private readonly FactionsViewModel _factions;
    private readonly TerrainViewModel _terrain;

    public MissionsViewModel(OperationFile file, OperationViewModel operation, FactionsViewModel factions,
        TeamsViewModel teams, TerrainViewModel terrain)
    {
        _file = file;
        _operation = operation;
        _factions = factions;
        _teams = teams;
        _terrain = terrain;
        Missions = new ObservableCollection<MissionViewModel>(file.LoadMissions().Select(m => new MissionViewModel(m, this)));

        operation.PropertyChanged += OnOperationChanged;
        teams.Items.CollectionChanged += OnTeamsChanged;
        teams.Removed += OnTeamRemoved;
        factions.Items.CollectionChanged += (_, _) => RebuildColumns();
        terrain.ZoneRemoved += OnZoneRemoved;
        foreach (var team in teams.Items)
            team.PropertyChanged += OnTeamPropertyChanged;
        foreach (var zone in terrain.Zones)
            zone.PropertyChanged += OnZonePropertyChanged;
        terrain.Zones.CollectionChanged += OnZonesChanged;

        RebuildColumns();
        Analyze();
    }

    public ObservableCollection<MissionViewModel> Missions { get; }

    /// <summary>Colonnes de la frise : les équipes, regroupées par faction.</summary>
    public ObservableCollection<TeamViewModel> Columns { get; } = [];

    public ObservableCollection<ZoneViewModel> Zones => _terrain.Zones;

    public int OperationStartMinutes => _operation.StartMinutes;

    public int OperationEndMinutes => _operation.EndMinutes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand), nameof(DuplicateCommand), nameof(ClearZoneCommand))]
    private MissionViewModel? _selected;

    public bool HasSelection => Selected is not null;

    [ObservableProperty]
    private IReadOnlyList<TeamChoiceViewModel> _teamChoices = [];

    [ObservableProperty]
    private IReadOnlyList<PredecessorChoiceViewModel> _predecessorChoices = [];

    [ObservableProperty]
    private string _issueSummary = "";

    // ----- Commandes -----

    /// <summary>Nouvelle mission : à la suite de la mission sélectionnée, sinon au début de l'OP.</summary>
    [RelayCommand]
    private void Add()
    {
        var start = Selected?.EndMinutes ?? OperationStartMinutes;
        var teams = Selected?.TeamIds.ToList() ?? Columns.Take(1).Select(t => t.Model.Id).ToList();
        CreateMission(start, teams);
    }

    /// <summary>Double-clic sur la frise : mission pour cette équipe, à cette heure.</summary>
    [RelayCommand]
    private void CreateAt(TimelineSlot slot) => CreateMission(slot.Minutes, [slot.Team.Model.Id]);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Duplicate()
    {
        var source = Selected!.Model;
        var copy = new Mission
        {
            Name = source.Name + " (copie)",
            Description = source.Description,
            ZoneId = source.ZoneId,
            TeamIds = [.. source.TeamIds],
            StartMinutes = source.EndMinutes,
            DurationMinutes = source.DurationMinutes,
            IsEssential = source.IsEssential,
        };
        AddMission(copy);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Remove()
    {
        var mission = Selected!;
        var index = Missions.IndexOf(mission);
        _file.Remove(mission.Model);
        Missions.Remove(mission);
        foreach (var other in Missions)
            other.SetPredecessor(mission.Model.Id, false);
        Selected = Missions.Count == 0 ? null : Missions[Math.Min(index, Missions.Count - 1)];
        Analyze();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void ClearZone() => Selected!.Zone = null;

    /// <summary>Appelé par une mission dont l'horaire, les équipes ou les prérequis changent.</summary>
    public void OnScheduleChanged() => Analyze();

    partial void OnSelectedChanged(MissionViewModel? value) => RebuildChoices();

    // ----- Interne -----

    private void CreateMission(int start, List<Guid> teamIds)
    {
        AddMission(new Mission
        {
            Name = $"Mission {Missions.Count + 1}",
            StartMinutes = start,
            DurationMinutes = DefaultDuration,
            TeamIds = teamIds,
        });
    }

    private void AddMission(Mission mission)
    {
        _file.Add(mission);
        var viewModel = new MissionViewModel(mission, this);
        Missions.Add(viewModel);
        Selected = viewModel;
        Analyze();
    }

    private void RebuildChoices()
    {
        if (Selected is not { } mission)
        {
            TeamChoices = [];
            PredecessorChoices = [];
            return;
        }

        var models = Missions.Select(m => m.Model).ToList();
        TeamChoices = Columns.Select(t => new TeamChoiceViewModel(t, mission)).ToList();
        PredecessorChoices = Missions
            .Where(m => m != mission)
            .OrderBy(m => m.StartMinutes)
            .Select(m => new PredecessorChoiceViewModel(m, mission,
                ScheduleAnalyzer.WouldCreateCycle(models, mission.Model.Id, m.Model.Id)))
            .ToList();
    }

    private void RebuildColumns()
    {
        var factionOrder = _factions.Items.Select((f, i) => (f.Model.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var ordered = _teams.Items
            .OrderBy(t => t.Model.FactionId is { } id && factionOrder.TryGetValue(id, out var index) ? index : int.MaxValue)
            .ThenBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        if (ordered.SequenceEqual(Columns))
            return;

        Columns.Clear();
        foreach (var team in ordered)
            Columns.Add(team);
        RebuildChoices();
    }

    private void Analyze()
    {
        var models = Missions.Select(m => m.Model).ToList();
        var issues = ScheduleAnalyzer.Analyze(models, OperationStartMinutes, OperationEndMinutes);
        var byMission = issues.ToLookup(i => i.MissionId);
        foreach (var mission in Missions)
            mission.Issues = byMission[mission.Model.Id].Select(Describe).Distinct().ToList();

        var count = Missions.Count(m => m.HasIssues);
        IssueSummary = count switch
        {
            0 when Missions.Count > 0 => "Planning cohérent",
            0 => "",
            1 => "1 mission à vérifier",
            _ => $"{count} missions à vérifier",
        };
    }

    private string Describe(ScheduleIssue issue)
    {
        var other = Missions.FirstOrDefault(m => m.Model.Id == issue.OtherMissionId)?.Name;
        var team = _teams.Items.FirstOrDefault(t => t.Model.Id == issue.TeamId)?.Name;
        return issue.Kind switch
        {
            ScheduleIssueKind.NoTeam => "Aucune équipe n'est affectée",
            ScheduleIssueKind.TeamOverlap => $"{team} est déjà sur « {other} » à ce moment",
            ScheduleIssueKind.PredecessorNotFinished => $"Commence avant la fin de « {other} »",
            ScheduleIssueKind.PredecessorDisabled => $"Le prérequis « {other} » est désactivé",
            ScheduleIssueKind.DependencyCycle => "Les prérequis forment une boucle",
            ScheduleIssueKind.OutsideOperation => "Déborde des horaires de l'OP",
            _ => issue.Kind.ToString(),
        };
    }

    private void OnOperationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OperationViewModel.StartMinutes) or nameof(OperationViewModel.EndMinutes))
        {
            OnPropertyChanged(e.PropertyName == nameof(OperationViewModel.StartMinutes)
                ? nameof(OperationStartMinutes)
                : nameof(OperationEndMinutes));
            Analyze();
        }
    }

    private void OnTeamsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var team in e.NewItems?.OfType<TeamViewModel>() ?? [])
            team.PropertyChanged += OnTeamPropertyChanged;
        foreach (var team in e.OldItems?.OfType<TeamViewModel>() ?? [])
            team.PropertyChanged -= OnTeamPropertyChanged;
        RebuildColumns();
    }

    private void OnTeamPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TeamViewModel.Faction) or nameof(TeamViewModel.Name))
            RebuildColumns();
    }

    private void OnTeamRemoved(TeamViewModel team)
    {
        foreach (var mission in Missions)
            mission.SetTeam(team.Model.Id, false);
    }

    private void OnZonesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var zone in e.NewItems?.OfType<ZoneViewModel>() ?? [])
            zone.PropertyChanged += OnZonePropertyChanged;
        foreach (var zone in e.OldItems?.OfType<ZoneViewModel>() ?? [])
            zone.PropertyChanged -= OnZonePropertyChanged;
    }

    private void OnZonePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(ZoneViewModel.Name) or nameof(ZoneViewModel.Color)))
            return;

        var zone = (ZoneViewModel)sender!;
        foreach (var mission in Missions.Where(m => m.Model.ZoneId == zone.Model.Id))
            mission.RefreshZone();
    }

    private void OnZoneRemoved(ZoneViewModel zone)
    {
        foreach (var mission in Missions.Where(m => m.Model.ZoneId == zone.Model.Id))
            mission.Zone = null;
    }
}
