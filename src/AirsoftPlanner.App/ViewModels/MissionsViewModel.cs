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
using AirsoftPlanner.Core.Localization;

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
    private readonly GameItemsViewModel _items;

    public MissionsViewModel(OperationFile file, OperationViewModel operation, FactionsViewModel factions,
        TeamsViewModel teams, TerrainViewModel terrain, GameItemsViewModel items)
    {
        _file = file;
        _operation = operation;
        _factions = factions;
        _teams = teams;
        _terrain = terrain;
        _items = items;
        foreach (var item in items.Items)
            item.PropertyChanged += OnItemChanged;
        items.Items.CollectionChanged += OnItemsChanged;
        items.Removed += OnItemRemoved;
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

        Delay = new DelayViewModel(this);
        RebuildColumns();
        Analyze();
    }

    /// <summary>Gestion d'un retard (décalage en cascade, missions optionnelles à désactiver).</summary>
    public DelayViewModel Delay { get; }

    public ObservableCollection<MissionViewModel> Missions { get; }

    /// <summary>Déclenché après chaque changement du planning (horaires, équipes, zones, activation...).</summary>
    public event Action? ScheduleChanged;

    /// <summary>Colonnes de la frise : les équipes, regroupées par faction.</summary>
    public ObservableCollection<TeamViewModel> Columns { get; } = [];

    public ObservableCollection<ZoneViewModel> Zones => _terrain.Zones;

    /// <summary>Terrain (fond de carte, zones) pour la carte de l'organisation.</summary>
    public TerrainViewModel Terrain => _terrain;

    /// <summary>Matériel de jeu affecté aux missions, placé sur la zone de chaque mission.</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private IReadOnlyList<ItemMarker> _planMarkers = [];

    private void RefreshPlanMarkers()
    {
        // Un repère par zone : matériel de toutes ses missions, quantités additionnées.
        PlanMarkers = Missions
            .Where(m => m.Model.IsEnabled && m.Zone is { IsComplete: true } && m.ItemUses.Count > 0)
            .GroupBy(m => m.Zone!)
            .Select(g =>
            {
                var zone = g.Key;
                var center = zone.IsArea ? Core.Geo.GeoMath.Centroid(zone.Points) : zone.Points[0];
                var items = g.SelectMany(m => m.ItemUses).GroupBy(u => u.Item.Name).Select(i => $"{i.Sum(u => u.Quantity)} × {i.Key}");
                // Légèrement au sud du point pour ne pas masquer son nom.
                return new ItemMarker(new Core.Domain.GeoPoint(center.Latitude - 0.0004, center.Longitude), $"📦 {string.Join(", ", items)}",
                    g.Contains(Selected));
            })
            .ToList();
    }

    public ObservableCollection<GameItemViewModel> GameItems => _items.Items;

    public OperationViewModel Operation => _operation;

    /// <summary>Élément choisi pour être ajouté au matériel de la mission sélectionnée.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddItemCommand))]
    private GameItemViewModel? _itemToAdd;

    [ObservableProperty]
    private decimal? _itemToAddQuantity = 1;

    public int OperationStartMinutes => _operation.StartMinutes;

    public int OperationEndMinutes => _operation.EndMinutes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand), nameof(DuplicateCommand), nameof(ClearZoneCommand), nameof(OpenDelayCommand))]
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
            Name = source.Name + L.T("copie"),
            Description = source.Description,
            ZoneId = source.ZoneId,
            TeamIds = [.. source.TeamIds],
            StartMinutes = source.EndMinutes,
            DurationMinutes = source.DurationMinutes,
            IsEssential = source.IsEssential,
            SuccessPoints = source.SuccessPoints,
            PartialPoints = source.PartialPoints,
            FailurePoints = source.FailurePoints,
            ConditionMissionId = source.ConditionMissionId,
            Condition = source.Condition,
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

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void OpenDelay() => Delay.Open(Selected!, 15);

    [RelayCommand(CanExecute = nameof(CanAddItem))]
    private void AddItem()
    {
        var current = Selected!.Model.Items.FirstOrDefault(u => u.ItemId == ItemToAdd!.Model.Id)?.Quantity ?? 0;
        Selected.SetItemQuantity(ItemToAdd!.Model.Id, current + Math.Max(1, (int)(ItemToAddQuantity ?? 1)));
    }

    [RelayCommand]
    private void RemoveItem(MissionItemUseViewModel use) => Selected?.SetItemQuantity(use.Item.Model.Id, 0);

    private bool CanAddItem => Selected is not null && ItemToAdd is not null;

    public int AssignedPlayers(MissionViewModel mission) =>
        _teams.Items.Where(t => mission.TeamIds.Contains(t.Model.Id)).Sum(t => t.Size);

    public GameItemViewModel? FindItem(Guid id) => _items.Items.FirstOrDefault(i => i.Model.Id == id);

    /// <summary>Appelé par une mission dont l'horaire, les équipes ou les prérequis changent.</summary>
    public void OnScheduleChanged() => Analyze();

    /// <summary>Résultat, barème ou condition d'une mission modifié : score et missions à proposer recalculés.</summary>
    public event Action? ResultsChanged;

    public void OnResultChanged() => ResultsChanged?.Invoke();

    /// <summary>Ferme la fiche de la mission (suivi de l'OP).</summary>
    [RelayCommand]
    private void ClearSelection() => Selected = null;

    /// <summary>Missions dont le résultat peut décider de la mission sélectionnée.</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private IReadOnlyList<MissionViewModel> _conditionChoices = [];

    partial void OnSelectedChanged(MissionViewModel? value)
    {
        RefreshPlanMarkers();
        RebuildChoices();
        AddItemCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Mission créée à la volée pendant l'OP (urgence) pour une équipe, à partir d'une heure donnée.</summary>
    public MissionViewModel CreateUrgentMission(TeamViewModel team, int start)
    {
        AddMission(new Mission
        {
            Name = $"URGENCE {MissionTime.Format(start)}",
            StartMinutes = start,
            DurationMinutes = DefaultDuration,
            TeamIds = [team.Model.Id],
            IsEssential = true,
            Description = L.T("mission_creee_pendant_l_op"),
        });
        return Selected!;
    }

    // ----- Interne -----

    private void CreateMission(int start, List<Guid> teamIds)
    {
        AddMission(new Mission
        {
            Name = L.F("mission_x", Missions.Count + 1),
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
            ConditionChoices = [];
            return;
        }

        ConditionChoices = Missions.Where(m => m != mission).OrderBy(m => m.StartMinutes).ToList();

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
            .Where(t => t.IsPlaying)
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
        var resources = new ScheduleResources(
            _teams.Items.ToDictionary(t => t.Model.Id, t => t.Size),
            _items.Items.ToDictionary(i => i.Model.Id, i => i.Model));
        var issues = ScheduleAnalyzer.Analyze(models, OperationStartMinutes, OperationEndMinutes, resources);
        var byMission = issues.ToLookup(i => i.MissionId);
        foreach (var mission in Missions)
            mission.Issues = byMission[mission.Model.Id].Select(Describe).Distinct().ToList();

        // Inventaire : dans quelles missions chaque élément est utilisé, et s'il en manque.
        var shortages = issues.Where(i => i.Kind == ScheduleIssueKind.ItemShortage).Select(i => i.ItemId).ToHashSet();
        foreach (var item in _items.Items)
        {
            item.Usages = Missions
                .Where(m => m.Model.Items.Any(u => u.ItemId == item.Model.Id))
                .OrderBy(m => m.StartMinutes)
                .Select(m => new GameItemUsage(m.Name, m.StartText, m.Model.Items.Where(u => u.ItemId == item.Model.Id).Sum(u => u.Quantity), m.IsEnabled))
                .ToList();
            item.SetShortage(shortages.Contains(item.Model.Id));
        }

        RefreshPlanMarkers();
        ScheduleChanged?.Invoke();
        var count = Missions.Count(m => m.HasIssues);
        IssueSummary = count switch
        {
            0 when Missions.Count > 0 => L.T("planning_coherent"),
            0 => "",
            1 => L.T("1_mission_a_verifier"),
            _ => L.F("x_missions_a_verifier", count),
        };
    }

    private string Describe(ScheduleIssue issue)
    {
        var other = Missions.FirstOrDefault(m => m.Model.Id == issue.OtherMissionId)?.Name;
        var team = _teams.Items.FirstOrDefault(t => t.Model.Id == issue.TeamId)?.Name;
        return issue.Kind switch
        {
            ScheduleIssueKind.NoTeam => L.T("aucune_equipe_n_est_affectee"),
            ScheduleIssueKind.TeamOverlap => L.F("x_est_deja_sur_x_a_ce_moment", team, other),
            ScheduleIssueKind.PredecessorNotFinished => L.F("commence_avant_la_fin_de_x", other),
            ScheduleIssueKind.ConditionNotFinished => L.F("commence_avant_le_resultat_de_x", other),
            ScheduleIssueKind.PredecessorDisabled => L.F("le_prerequis_x_est_desactive", other),
            ScheduleIssueKind.DependencyCycle => L.T("les_prerequis_forment_une_boucle"),
            ScheduleIssueKind.OutsideOperation => L.T("deborde_des_horaires_de_l_op"),
            ScheduleIssueKind.TooManyPlayers => L.T("effectif_maximum_depasse"),
            ScheduleIssueKind.ItemShortage => FindItem(issue.ItemId ?? Guid.Empty) is { } item
                ? item.IsConsumable
                    ? L.F("pas_assez_de_x_pour_toute_l_op_stock_x", item.Name, item.Model.Quantity)
                    : L.F("pas_assez_de_x_pour_les_missions_simultanees_sto", item.Name, item.Model.Quantity)
                : L.T("materiel_insuffisant"),
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
        if (e.PropertyName is nameof(TeamViewModel.Faction) or nameof(TeamViewModel.Name) or nameof(TeamViewModel.Status))
            RebuildColumns();
        if (e.PropertyName == nameof(TeamViewModel.Size))
        {
            foreach (var mission in Missions)
                mission.RefreshPlayers();
            Analyze();
        }
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

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var item in e.NewItems?.OfType<GameItemViewModel>() ?? [])
            item.PropertyChanged += OnItemChanged;
        foreach (var item in e.OldItems?.OfType<GameItemViewModel>() ?? [])
            item.PropertyChanged -= OnItemChanged;
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GameItemViewModel.Quantity) or nameof(GameItemViewModel.IsConsumable))
            Analyze();
        else if (e.PropertyName == nameof(GameItemViewModel.Name))
            foreach (var mission in Missions)
                mission.RefreshItems();
    }

    private void OnItemRemoved(GameItemViewModel item)
    {
        foreach (var mission in Missions.Where(m => m.Model.Items.Any(u => u.ItemId == item.Model.Id)))
            mission.SetItemQuantity(item.Model.Id, 0);
    }
}
