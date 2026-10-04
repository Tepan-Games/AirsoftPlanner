using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Core.Geo;
using AirsoftPlanner.Core.Planning;
using AirsoftPlanner.Data;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AirsoftPlanner.Core.Localization;

namespace AirsoftPlanner.App.ViewModels;

/// <summary>Une équipe dans le plan radio.</summary>
public record RadioTeam(string Name, string Frequency, bool IsCommand, bool IsDuplicate = false);

/// <summary>Une faction dans le plan radio : sa fréquence de commandement et celles de ses équipes.</summary>
public record RadioFaction(string Name, string Color, string Frequency, IReadOnlyList<RadioTeam> Teams, bool IsDuplicate = false);

public record OutReasonOption(OutReason Value, string Label)
{
    public static IReadOnlyList<OutReasonOption> All { get; } =
    [
        new(OutReason.RealInjury, L.T("blessure_reelle")),
        new(OutReason.Rest, L.T("pause_fatigue")),
        new(OutReason.Equipment, L.T("probleme_de_materiel")),
        new(OutReason.Sanction, L.T("sanction")),
        new(OutReason.Abandon, L.T("abandon_depart")),
        new(OutReason.Other, L.T("autre")),
    ];

    public static OutReasonOption Of(OutReason reason) => All.First(o => o.Value == reason);

    public override string ToString() => Label;
}

/// <summary>Un joueur de l'équipe sélectionnée, en jeu ou hors jeu.</summary>
public record PlayerRow(MemberViewModel? Member, string Name, bool IsOut, string StateText);

/// <summary>Parcours d'une équipe sur la carte.</summary>
public record TeamTrail(string Color, IReadOnlyList<GeoPoint> Points);

/// <summary>Vitesse de lecture de la simulation (minutes d'OP par seconde réelle).</summary>
public record PlaybackSpeed(double MinutesPerSecond, string Label)
{
    public static IReadOnlyList<PlaybackSpeed> All { get; } =
    [
        new(1 / 60.0, L.T("1_temps_reel")),
        new(10 / 60.0, "× 10"),
        new(1, L.T("60_1_min_s")),
        new(5, L.T("300_5_min_s")),
        new(15, L.T("900_15_min_s")),
    ];

    public override string ToString() => Label;
}

/// <summary>Objet d'objectif affiché sur la carte.</summary>
/// <param name="SymbolColor">Véhicule : couleur de la faction (symbole militaire) ; null pour un objet.</param>
public record ItemMarker(GeoPoint Point, string Label, bool IsSelected, string? SymbolColor = null);

/// <summary>Option d'événement pour un objet d'objectif.</summary>
public record ItemEventOption(ItemEventKind Kind, string Label, bool NeedsTeam, bool NeedsLocation)
{
    public static IReadOnlyList<ItemEventOption> All { get; } =
    [
        new(ItemEventKind.Placed, L.T("place_sur_le_terrain"), false, true),
        new(ItemEventKind.PickedUp, L.T("recupere_par"), true, false),
        new(ItemEventKind.Transferred, L.T("passe_a"), true, false),
        new(ItemEventKind.Dropped, L.T("depose_abandonne"), false, true),
        new(ItemEventKind.Lost, L.T("perdu"), false, false),
        new(ItemEventKind.Returned, L.T("rendu_a_l_orga"), false, false),
    ];

    public static ItemEventOption Of(ItemEventKind kind) => All.First(o => o.Kind == kind);

    public override string ToString() => Label;
}

/// <summary>Un objet d'objectif suivi : où il est, qui l'a, depuis quand, et son historique.</summary>
public partial class TrackedItemViewModel(GameItemViewModel item) : ViewModelBase
{
    public GameItemViewModel Item => item;

    [ObservableProperty]
    private string _stateText = "";

    [ObservableProperty]
    private string _locationText = "";

    [ObservableProperty]
    private string _stateColor = "#9E9E9E";

    [ObservableProperty]
    private GeoPoint? _location;

    [ObservableProperty]
    private IReadOnlyList<string> _history = [];
}

/// <summary>Équipe affichée sur la carte de suivi.</summary>
public record TeamMarker(GeoPoint Point, string Color, string Label, string StatusColor, GeoPoint? Target, bool IsSelected,
    AirsoftPlanner.Core.Symbols.MilSymbol Symbol = AirsoftPlanner.Core.Symbols.MilSymbol.Infantry,
    AirsoftPlanner.Core.Symbols.Echelon Echelon = AirsoftPlanner.Core.Symbols.Echelon.None);

/// <summary>État d'une équipe à l'instant suivi.</summary>
public partial class TeamStatusViewModel(TeamViewModel team) : ViewModelBase
{
    private static CultureInfo French => AirsoftPlanner.Core.Localization.L.Culture;

    public TeamViewModel Team => team;

    /// <summary>Dernière évaluation, utilisée pour proposer le retard à répercuter.</summary>
    public TeamProgress? Progress { get; private set; }

    [ObservableProperty]
    private ProgressStatus _status;

    [ObservableProperty]
    private GeoPoint? _position;

    [ObservableProperty]
    private GeoPoint? _target;

    [ObservableProperty]
    private string _missionText = "";

    [ObservableProperty]
    private string _distanceText = "";

    [ObservableProperty]
    private string _positionText = "";

    [ObservableProperty]
    private string _strengthText = "";

    /// <summary>Au moins un joueur de l'équipe est hors jeu pour une blessure réelle.</summary>
    [ObservableProperty]
    private bool _hasRealInjury;

    public string StatusLabel => Status switch
    {
        ProgressStatus.OnTime => L.T("a_l_heure_2"),
        ProgressStatus.Tight => L.T("juste"),
        ProgressStatus.Late => L.T("en_retard"),
        ProgressStatus.Idle => L.T("sans_mission"),
        _ => L.T("position_inconnue"),
    };

    public string StatusColor => Status switch
    {
        ProgressStatus.OnTime => "#2E7D32",
        ProgressStatus.Tight => "#F9A825",
        ProgressStatus.Late => "#D32F2F",
        ProgressStatus.Idle => "#607D8B",
        _ => "#9E9E9E",
    };

    partial void OnStatusChanged(ProgressStatus value)
    {
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(StatusColor));
    }

    public void Update(TeamProgress progress, GeoPoint? position, Func<Mission, string> describeMission, Func<GeoPoint, string> formatPoint)
    {
        Progress = progress;
        Status = progress.Status;
        Position = position;

        MissionText = (progress.CurrentMission, progress.TargetMission) switch
        {
            ({ } current, _) => L.F("en_cours_x", describeMission(current)),
            (null, { } next) => L.F("prochaine_x", describeMission(next)),
            _ => L.T("aucune_mission_a_venir"),
        };

        DistanceText = progress switch
        {
            { DistanceMeters: 0 } => L.T("sur_la_zone"),
            { DistanceMeters: { } distance, TravelMinutes: { } travel, SlackMinutes: { } slack } =>
                string.Format(French, L.T("x_x_a_pied_x"), FormatDistance(distance), MissionTime.FormatDuration((int)Math.Ceiling(travel)),
                    progress.CurrentMission is not null ? L.T("mission_commencee")
                    : slack >= 0 ? $"marge {MissionTime.FormatDuration((int)slack)}"
                    : L.F("retard_estime_x", MissionTime.FormatDuration((int)Math.Ceiling(-slack)))),
            _ => "",
        };

        PositionText = position is not { } point ? L.T("aucune_position_recue")
            : $"{formatPoint(point)} · {FormatAge(progress.PositionAgeMinutes ?? 0)}";
    }

    private static string FormatDistance(double meters) =>
        meters < 1000 ? $"{meters:0} m" : string.Format(French, L.T("x_km"), meters / 1000);

    private static string FormatAge(double minutes) => minutes switch
    {
        < 1 => L.T("a_l_instant"),
        < 60 => L.F("il_y_a_x_min", minutes),
        _ => L.F("il_y_a_x", MissionTime.FormatDuration((int)minutes)),
    };
}

/// <summary>
/// Suivi de l'OP : heure réelle ou simulée, dernières positions des équipes, et alerte quand une équipe
/// ne pourra pas rejoindre à temps la zone de sa prochaine mission.
/// </summary>
public partial class TrackingViewModel : ViewModelBase
{
    private readonly OperationFile _file;
    private readonly OperationViewModel _operation;
    private readonly TeamsViewModel _teams;
    private readonly TerrainViewModel _terrain;
    private readonly List<TeamPosition> _positions;
    private readonly DispatcherTimer _clock;

    private readonly GameItemsViewModel _items;
    private readonly List<ItemEvent> _itemEvents;
    private readonly List<PlayerStatusEvent> _playerEvents;

    public TrackingViewModel(OperationFile file, OperationViewModel operation, TeamsViewModel teams,
        TerrainViewModel terrain, MissionsViewModel missions, GameItemsViewModel items)
    {
        _items = items;
        _itemEvents = file.LoadItemEvents().ToList();
        _playerEvents = file.LoadPlayerStatusEvents().ToList();
        items.Items.CollectionChanged += (_, _) => RebuildTrackedItems();
        foreach (var item in items.Items)
            item.PropertyChanged += OnItemChanged;
        items.Items.CollectionChanged += (_, e) =>
        {
            foreach (var item in e.NewItems?.OfType<GameItemViewModel>() ?? [])
                item.PropertyChanged += OnItemChanged;
        };
        _file = file;
        _operation = operation;
        _teams = teams;
        _terrain = terrain;
        Missions = missions;
        _positions = file.LoadPositions().ToList();

        // Hors des horaires de l'OP, on démarre en simulation au début de l'OP.
        var realNow = operation.ToMinutes(DateTime.Now);
        _isSimulation = realNow < operation.StartMinutes || realNow > operation.EndMinutes;
        _simulatedMinutes = operation.StartMinutes;

        RebuildTrackedItems();
        RebuildStatuses();
        missions.Columns.CollectionChanged += (_, _) => RebuildStatuses();
        missions.ScheduleChanged += Refresh;
        operation.PropertyChanged += OnOperationChanged;
        terrain.Zones.CollectionChanged += OnZonesChanged;
        foreach (var zone in terrain.Zones)
            zone.PropertyChanged += OnZoneChanged;

        _clock = new DispatcherTimer(TimeSpan.FromSeconds(15), DispatcherPriority.Background, (_, _) =>
        {
            if (!IsSimulation)
                Refresh();
        });
        _clock.Start();
        Refresh();
    }

    public MissionsViewModel Missions { get; }

    /// <summary>Positions, objets et effectifs enregistrés pendant l'OP (RETEX).</summary>
    public IReadOnlyList<TeamPosition> RecordedPositions => _positions;

    public IReadOnlyList<ItemEvent> RecordedItemEvents => _itemEvents;

    public IReadOnlyList<PlayerStatusEvent> RecordedPlayerEvents => _playerEvents;

    private DispatchViewModel? _dispatch;

    /// <summary>Messages de l'orga et diffusion des missions aux téléphones.</summary>
    public DispatchViewModel? Dispatch
    {
        get => _dispatch;
        set
        {
            _dispatch = value;
            value?.Refresh(NowMinutes);
        }
    }

    public TerrainViewModel Terrain => _terrain;

    public ObservableCollection<TeamStatusViewModel> Statuses { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NowMinutes), nameof(NowText))]
    private bool _isSimulation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NowMinutes), nameof(NowText))]
    private double _simulatedMinutes;

    // ----- Lecture de la simulation : lecture, avance rapide, retour -----

    private System.Threading.Timer? _player;

    public IReadOnlyList<PlaybackSpeed> PlaybackSpeeds => PlaybackSpeed.All;

    [ObservableProperty]
    private PlaybackSpeed _playbackSpeed = PlaybackSpeed.All[2];

    /// <summary>1 : lecture, -1 : retour, 0 : pause.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlaying))]
    private int _playDirection;

    public bool IsPlaying => PlayDirection != 0;

    [RelayCommand]
    private void PlayForward() => Play(1);

    [RelayCommand]
    private void PlayBackward() => Play(-1);

    [RelayCommand]
    private void PausePlayback() => PlayDirection = 0;

    /// <summary>Saut de 5 minutes en avant (+) ou en arrière (-).</summary>
    [RelayCommand]
    private void Step(string minutes)
    {
        IsSimulation = true;
        SimulatedMinutes = Math.Clamp(SimulatedMinutes + double.Parse(minutes, System.Globalization.CultureInfo.InvariantCulture), SimulationStart, SimulationEnd);
    }

    private void Play(int direction)
    {
        IsSimulation = true;
        // Lecture depuis la fin (ou retour depuis le début) : on repart de l'autre bout.
        if (direction > 0 && SimulatedMinutes >= SimulationEnd)
            SimulatedMinutes = SimulationStart;
        if (direction < 0 && SimulatedMinutes <= SimulationStart)
            SimulatedMinutes = SimulationEnd;
        PlayDirection = direction;
        _player ??= new System.Threading.Timer(_ => Dispatcher.UIThread.Post(AdvancePlayback));
        _player.Change(TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(200));
    }

    private void AdvancePlayback()
    {
        if (PlayDirection == 0 || !IsSimulation)
        {
            PlayDirection = 0;
            _player?.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
            return;
        }

        var next = SimulatedMinutes + PlayDirection * PlaybackSpeed.MinutesPerSecond * 0.2;
        SimulatedMinutes = Math.Clamp(next, SimulationStart, SimulationEnd);
        if (next <= SimulationStart || next >= SimulationEnd)
            PlayDirection = 0;
    }

    public double SimulationStart => _operation.StartMinutes;

    public double SimulationEnd => _operation.EndMinutes;

    /// <summary>Instant suivi, en minutes depuis minuit le premier jour de l'OP.</summary>
    public double NowMinutes => IsSimulation ? SimulatedMinutes : _operation.ToMinutes(DateTime.Now);

    public string NowText => (IsSimulation ? L.T("simulation_2") : L.T("maintenant"))
        + _operation.ToDateTime(NowMinutes).ToString("dddd d MMMM HH:mm", AirsoftPlanner.Core.Localization.L.Culture);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SetPositionCommand), nameof(PlanDelayCommand), nameof(PlayerOutCommand), nameof(PlayerBackCommand))]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private TeamStatusViewModel? _selected;

    public bool HasSelection => Selected is not null;

    /// <summary>En mode placement, un clic sur la carte enregistre la position de l'équipe sélectionnée.</summary>
    [ObservableProperty]
    private bool _isPlacing;

    [ObservableProperty]
    private string _positionInput = "";

    [ObservableProperty]
    private IReadOnlyList<TeamMarker> _markers = [];

    [ObservableProperty]
    private string _summary = "";

    // ----- Effectif en jeu -----

    public IReadOnlyList<OutReasonOption> OutReasons => OutReasonOption.All;

    [ObservableProperty]
    private OutReasonOption _selectedOutReason = OutReasonOption.Of(OutReason.Rest);

    [ObservableProperty]
    private string _outNotes = "";

    /// <summary>Nombre de joueurs non nommés à sortir ou faire revenir (équipes sans membres saisis).</summary>
    [ObservableProperty]
    private decimal? _anonymousCount = 1;

    [ObservableProperty]
    private IReadOnlyList<PlayerRow> _players = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayerOutCommand), nameof(PlayerBackCommand))]
    private PlayerRow? _selectedPlayer;

    [ObservableProperty]
    private string _strengthHistory = "";

    public bool SelectedTeamHasMembers => Selected?.Team.HasMembers == true;

    [RelayCommand(CanExecute = nameof(CanChangePlayer))]
    private void PlayerOut() => AddPlayerEvent(isOut: true);

    [RelayCommand(CanExecute = nameof(CanChangePlayer))]
    private void PlayerBack() => AddPlayerEvent(isOut: false);

    private bool CanChangePlayer => Selected is not null && (!SelectedTeamHasMembers || SelectedPlayer?.Member is not null);

    private void AddPlayerEvent(bool isOut)
    {
        var team = Selected!.Team;
        var playerEvent = new PlayerStatusEvent
        {
            TeamId = team.Model.Id,
            MemberId = SelectedTeamHasMembers ? SelectedPlayer?.Member?.Model.Id : null,
            IsOut = isOut,
            Reason = SelectedOutReason.Value,
            Players = SelectedTeamHasMembers ? 1 : Math.Max(1, (int)(AnonymousCount ?? 1)),
            At = new DateTimeOffset(_operation.ToDateTime(NowMinutes)),
            Notes = isOut ? OutNotes.Trim() : "",
        };
        _file.Add(playerEvent);
        _playerEvents.Add(playerEvent);
        OutNotes = "";
        Refresh();
    }

    private TeamStrength StrengthOf(TeamViewModel team, DateTimeOffset at) =>
        StrengthTracker.StrengthAt(team.Size, _playerEvents.Where(e => e.TeamId == team.Model.Id), at);

    /// <summary>« Blessure réelle depuis 10:12 (cheville) ».</summary>
    private string OutDescription(OutPlayer player)
    {
        var since = MissionTime.Format((int)Math.Round(_operation.ToMinutes(player.Since.LocalDateTime)));
        return L.F("hors_jeu_x_depuis_x_x", OutReasonOption.Of(player.Reason).Label.ToLowerInvariant(), since, (player.Notes.Length > 0 ? $" ({player.Notes})" : ""));
    }

    private void RefreshPlayers()
    {
        OnPropertyChanged(nameof(SelectedTeamHasMembers));
        if (Selected?.Team is not { } team)
        {
            Players = [];
            StrengthHistory = "";
            return;
        }

        var now = new DateTimeOffset(_operation.ToDateTime(NowMinutes));
        var strength = StrengthOf(team, now);
        var selectedMember = SelectedPlayer?.Member;
        Players = team.Members
            .Select(m => strength.Out.FirstOrDefault(o => o.MemberId == m.Model.Id) is { } o
                ? new PlayerRow(m, m.DisplayName, true, OutDescription(o))
                : new PlayerRow(m, m.DisplayName, false, L.T("en_jeu")))
            .Concat(strength.Out.Where(o => o.MemberId is null)
                .Select(o => new PlayerRow(null, L.F("x_joueur_s_non_nomme_s", o.Players), true, OutDescription(o))))
            .ToList();
        SelectedPlayer = Players.FirstOrDefault(p => p.Member is not null && p.Member == selectedMember);

        var events = _playerEvents.Where(e => e.TeamId == team.Model.Id && e.At <= now).ToList();
        var timeline = StrengthTracker.Timeline(team.Size, events);
        StrengthHistory = timeline.Count == 0
            ? L.F("effectif_complet_x_depuis_le_debut_de_l_op", team.Size)
            : L.F("debut_x_2", team.Size) + string.Join(" → ", timeline.Select(t =>
                $"{MissionTime.Format((int)Math.Round(_operation.ToMinutes(t.At.LocalDateTime)))} : {t.Present}"));
    }

    // ----- Mission urgente -----

    /// <summary>En attente d'un clic sur la carte pour situer l'urgence.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMapPlacing))]
    private bool _isPlacingUrgent;

    private MissionViewModel? _urgentMission;

    /// <summary>
    /// Crée immédiatement une mission pour l'équipe sélectionnée, à l'heure suivie (arrondie aux 5 minutes),
    /// puis attend un clic sur la carte pour situer l'intervention (Échap ou nouveau clic sur le bouton pour s'en passer).
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void CreateUrgentMission()
    {
        var start = (int)Math.Ceiling(NowMinutes / 5) * 5;
        _urgentMission = Missions.CreateUrgentMission(Selected!.Team, start);
        Dispatch?.ProposeUrgent(Selected.Team, _urgentMission.Model);
        IsPlacing = false;
        IsPlacingItem = false;
        IsPlacingUrgent = true;
    }

    // ----- Trajets -----

    /// <summary>Affiche le parcours de chaque équipe depuis le début de l'OP jusqu'à l'instant suivi.</summary>
    [ObservableProperty]
    private bool _showTrails;

    [ObservableProperty]
    private IReadOnlyList<TeamTrail> _trails = [];

    partial void OnShowTrailsChanged(bool value) => RefreshMarkers();

    // ----- Objets d'objectif -----

    public ObservableCollection<TrackedItemViewModel> TrackedItems { get; } = [];

    public IReadOnlyList<ItemEventOption> ItemEventOptions => ItemEventOption.All;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RecordItemEventCommand))]
    [NotifyPropertyChangedFor(nameof(HasSelectedItem))]
    private TrackedItemViewModel? _selectedItem;

    public bool HasSelectedItem => SelectedItem is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ItemEventNeedsTeam), nameof(ItemEventNeedsLocation))]
    private ItemEventOption _selectedItemEvent = ItemEventOption.Of(ItemEventKind.PickedUp);

    public bool ItemEventNeedsTeam => SelectedItemEvent.NeedsTeam;

    public bool ItemEventNeedsLocation => SelectedItemEvent.NeedsLocation;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RecordItemEventCommand))]
    private TeamStatusViewModel? _itemEventTeam;

    /// <summary>En attente d'un clic sur la carte pour placer l'objet sélectionné.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMapPlacing))]
    private bool _isPlacingItem;

    [ObservableProperty]
    private IReadOnlyList<ItemMarker> _itemMarkers = [];

    /// <summary>La carte attend un clic (position d'équipe ou d'objet).</summary>
    public bool IsMapPlacing
    {
        get => IsPlacing || IsPlacingItem || IsPlacingUrgent;
        set
        {
            if (!value)
            {
                IsPlacing = false;
                IsPlacingItem = false;
                IsPlacingUrgent = false;
            }
        }
    }

    partial void OnIsPlacingChanged(bool value) => OnPropertyChanged(nameof(IsMapPlacing));

    partial void OnSelectedItemChanged(TrackedItemViewModel? value) => RefreshMarkers();

    /// <summary>
    /// Enregistre ce qui arrive à l'objet sélectionné. Un objet récupéré ou passé suit ensuite l'équipe ;
    /// un objet placé ou déposé demande un clic sur la carte.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRecordItemEvent))]
    private void RecordItemEvent()
    {
        if (SelectedItemEvent.NeedsLocation)
        {
            IsPlacing = false;
            IsPlacingItem = true;
            return;
        }

        var team = SelectedItemEvent.NeedsTeam ? ItemEventTeam?.Team : null;
        AddItemEvent(SelectedItem!, SelectedItemEvent.Kind, team, location: team is null ? CurrentItemLocation(SelectedItem!) : LastPosition(team.Model.Id));
    }

    private bool CanRecordItemEvent => SelectedItem is not null && (!SelectedItemEvent.NeedsTeam || ItemEventTeam is not null);

    partial void OnSelectedItemEventChanged(ItemEventOption value) => RecordItemEventCommand.NotifyCanExecuteChanged();

    private void AddItemEvent(TrackedItemViewModel item, ItemEventKind kind, TeamViewModel? team, GeoPoint? location)
    {
        var itemEvent = new ItemEvent
        {
            ItemId = item.Item.Model.Id,
            Kind = kind,
            TeamId = team?.Model.Id,
            Location = location,
            At = new DateTimeOffset(_operation.ToDateTime(NowMinutes)),
        };
        _file.Add(itemEvent);
        _itemEvents.Add(itemEvent);
        Refresh();
    }

    private GeoPoint? CurrentItemLocation(TrackedItemViewModel item) => item.Location;

    private GeoPoint? LastPosition(Guid teamId)
    {
        var now = NowMinutes;
        return _positions.LastOrDefault(p => p.TeamId == teamId && _operation.ToMinutes(p.ReceivedAt.LocalDateTime) <= now + 0.01)?.Point;
    }

    private void RebuildTrackedItems()
    {
        var selected = SelectedItem?.Item;
        TrackedItems.Clear();
        foreach (var item in _items.Items.Where(i => i.IsTracked))
            TrackedItems.Add(new TrackedItemViewModel(item));
        SelectedItem = TrackedItems.FirstOrDefault(t => t.Item == selected) ?? TrackedItems.FirstOrDefault();
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GameItemViewModel.IsTracked))
        {
            RebuildTrackedItems();
            Refresh();
        }
    }

    private void RefreshItems()
    {
        var now = new DateTimeOffset(_operation.ToDateTime(NowMinutes));
        var teams = _teams.Items.ToDictionary(t => t.Model.Id);
        string TeamName(Guid? id) => id is { } t && teams.TryGetValue(t, out var team) ? team.Name : L.T("equipe_inconnue");
        string Time(DateTimeOffset at) => MissionTime.Format((int)Math.Round(_operation.ToMinutes(at.LocalDateTime)));

        foreach (var tracked in TrackedItems)
        {
            var events = _itemEvents.Where(e => e.ItemId == tracked.Item.Model.Id).ToList();
            var state = ItemTracker.StateAt(events, now, (team, at) => LastPosition(team));
            tracked.Location = state.Location;
            tracked.StateText = state.Kind switch
            {
                null => L.T("pas_encore_suivi"),
                ItemEventKind.Placed => L.F("sur_le_terrain_depuis_x", Time(state.Since!.Value)),
                ItemEventKind.PickedUp or ItemEventKind.Transferred => L.F("detenu_par_x_depuis_x", TeamName(state.HolderTeamId), Time(state.Since!.Value)),
                ItemEventKind.Dropped => L.F("depose_a_x", Time(state.Since!.Value)),
                ItemEventKind.Lost => L.F("perdu_signale_a_x", Time(state.Since!.Value)),
                _ => L.F("rendu_a_l_orga_a_x", Time(state.Since!.Value)),
            };
            tracked.StateColor = state.Kind switch
            {
                ItemEventKind.PickedUp or ItemEventKind.Transferred => "#1565C0",
                ItemEventKind.Placed or ItemEventKind.Dropped => "#EF6C00",
                ItemEventKind.Lost => "#C62828",
                ItemEventKind.Returned => "#2E7D32",
                _ => "#9E9E9E",
            };
            tracked.LocationText = state.Location is { } point && state.OnField
                ? L.F("a_recuperer_x", Coordinates.Format(point, _operation.CoordinateFormat))
                : state.OnField ? L.T("position_inconnue") : "";
            tracked.History = events.Where(e => e.At <= now).OrderByDescending(e => e.At)
                .Select(e => $"{Time(e.At)} — {ItemEventOption.Of(e.Kind).Label}{(e.TeamId is null ? "" : " " + TeamName(e.TeamId))}"
                             + (e.Location is { } l ? $" ({Coordinates.Format(l, _operation.CoordinateFormat)})" : ""))
                .ToList();
        }
    }

    /// <summary>
    /// Dernière position de chaque équipe, republiée par le serveur local pour les autres postes.
    /// Remplacée d'un bloc à chaque rafraîchissement : lisible sans risque depuis le fil du serveur.
    /// </summary>
    public IReadOnlyList<Services.Gps.PublishedPosition> PublishedPositions { get; private set; } = [];

    /// <summary>Dernière position reçue de chaque équipe en jeu.</summary>
    public IReadOnlyList<(TeamViewModel Team, GeoPoint Point, DateTimeOffset Time)> LatestPositions() => Statuses
        .Select(s => (s.Team, Position: _positions.LastOrDefault(p => p.TeamId == s.Team.Model.Id)))
        .Where(x => x.Position is not null)
        .Select(x => (x.Team, x.Position!.Point, x.Position.ReceivedAt))
        .ToList();

    /// <summary>Équipes en jeu, pour la page de saisie du serveur local.</summary>
    public IReadOnlyList<string> PublishedTeamNames { get; private set; } = [];

    /// <summary>Plan radio affiché en permanence sur l'écran de suivi.</summary>
    [ObservableProperty]
    private IReadOnlyList<RadioFaction> _radioPlan = [];

    [RelayCommand]
    private void MapClicked(GeoPoint point)
    {
        if (IsPlacingUrgent && _urgentMission is { } urgent)
        {
            urgent.Zone = Terrain.AddPointZone(L.F("urgence_x", MissionTime.Format(urgent.StartMinutes)), point, "#C62828");
            IsPlacingUrgent = false;
            _urgentMission = null;
            Refresh();
            return;
        }

        if (IsPlacingItem && SelectedItem is { } item)
        {
            AddItemEvent(item, SelectedItemEvent.Kind, null, point);
            IsPlacingItem = false;
            return;
        }

        if (!IsPlacing || Selected is null)
            return;

        RecordPosition(Selected.Team, point);
        IsPlacing = false;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void SetPosition()
    {
        if (!Coordinates.TryParse(PositionInput, out var point))
            throw new FormatException(L.T("coordonnees_non_reconnues"));

        RecordPosition(Selected!.Team, point);
        PositionInput = "";
    }

    /// <summary>
    /// Ouvre la gestion du retard pour la mission visée par l'équipe sélectionnée, avec le retard estimé :
    /// temps de trajet restant si la mission a commencé, sinon le dépassement de la marge.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void PlanDelay()
    {
        if (Selected?.Progress is not { TargetMission: { } target } progress)
            return;

        var mission = Missions.Missions.FirstOrDefault(m => m.Model.Id == target.Id);
        if (mission is null)
            return;

        var estimate = progress.CurrentMission is not null
            ? progress.TravelMinutes ?? 0
            : -(progress.SlackMinutes ?? 0);
        Missions.Delay.Open(mission, (int)Math.Ceiling(Math.Max(5, estimate) / 5) * 5);
    }

    partial void OnIsSimulationChanged(bool value) => Refresh();

    partial void OnSimulatedMinutesChanged(double value) => Refresh();

    /// <summary>Traces des véhicules mis en jeu (affichés sur la carte).</summary>
    public VehicleTracker? Vehicles { get; set; }

    /// <summary>Réception automatique des positions (smartphones, Meshtastic, Traccar, fichiers).</summary>
    public GpsViewModel? Gps { get; set; }

    partial void OnSelectedChanged(TeamStatusViewModel? value)
    {
        Gps?.NotifyTeamSelectionChanged();
        RefreshMarkers();
        RefreshPlayers();
        CreateUrgentMissionCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Enregistre une position reçue (saisie manuelle aujourd'hui, GPS plus tard).</summary>
    /// <summary>Orgas de l'OP (positions de leurs téléphones sur la carte).</summary>
    public OrganizersViewModel? Organizers { get; set; }

    /// <summary>Position du téléphone d'un orga (enregistrée comme celle d'une équipe, avec l'identifiant de l'orga).</summary>
    public void RecordOrganizerPosition(OrganizerViewModel organizer, GeoPoint point, DateTimeOffset? at)
    {
        var position = new TeamPosition
        {
            TeamId = organizer.Model.Id,
            Point = point,
            ReceivedAt = at ?? DateTimeOffset.Now,
            Source = L.T("telephone_orga"),
        };
        _file.Add(position);
        var index = _positions.FindLastIndex(p => p.ReceivedAt <= position.ReceivedAt);
        _positions.Insert(index + 1, position);
        RefreshMarkers();
    }

    public void RecordPosition(TeamViewModel team, GeoPoint point, string source = "Manuel", DateTimeOffset? at = null)
    {
        // Saisie manuelle : heure suivie (réelle ou simulée). GPS : heure de la mesure.
        var position = new TeamPosition
        {
            TeamId = team.Model.Id,
            Point = point,
            ReceivedAt = at ?? new DateTimeOffset(_operation.ToDateTime(NowMinutes)),
            Source = source,
        };
        _file.Add(position);
        // Les positions arrivent dans le désordre (fichiers, retard réseau) : la liste reste chronologique.
        var index = _positions.FindLastIndex(p => p.ReceivedAt <= position.ReceivedAt);
        _positions.Insert(index + 1, position);
        Refresh();
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(NowMinutes));
        OnPropertyChanged(nameof(NowText));
        var now = NowMinutes;
        var zones = _terrain.Zones.ToDictionary(z => z.Model.Id, z => z.Model);
        var missions = Missions.Missions.Select(m => m.Model).ToList();
        var walking = (double)(_operation.WalkingSpeedKmh ?? 3);
        var driving = (double)(_operation.VehicleSpeedKmh ?? 25);

        foreach (var status in Statuses)
        {
            // Dernière position reçue avant l'instant suivi (permet de rejouer l'OP en simulation).
            var last = _positions.LastOrDefault(p => p.TeamId == status.Team.Model.Id
                                                     && _operation.ToMinutes(p.ReceivedAt.LocalDateTime) <= now + 0.01);
            (GeoPoint, double)? known = last is null ? null : (last.Point, _operation.ToMinutes(last.ReceivedAt.LocalDateTime));
            // Équipe motorisée (véhicule mis en jeu) : vitesse estimée en véhicule.
            var speed = status.Team.Vehicles.Any(v => v.InGame) ? driving : walking;
            var progress = ProgressTracker.Evaluate(status.Team.Model.Id, missions, zones, known, now, speed);
            status.Update(progress, last?.Point, DescribeMission, p => Coordinates.Format(p, _operation.CoordinateFormat));
            var strength = StrengthOf(status.Team, new DateTimeOffset(_operation.ToDateTime(now)));
            status.StrengthText = strength.OutCount == 0
                ? L.F("effectif_x_x", strength.Present, status.Team.Size)
                : L.F("effectif_x_x_x_hors_jeu", strength.Present, status.Team.Size, strength.OutCount);
            status.HasRealInjury = strength.Out.Any(o => o.Reason == OutReason.RealInjury);
            status.Target = progress.TargetMission?.ZoneId is { } zoneId && zones.TryGetValue(zoneId, out var zone) && zone.Points.Count > 0
                ? GeoMath.Centroid(zone.Points)
                : null;
        }

        Dispatch?.Refresh(now);
        var late = Statuses.Count(s => s.Status == ProgressStatus.Late);
        var tight = Statuses.Count(s => s.Status == ProgressStatus.Tight);
        var unknown = Statuses.Count(s => s.Status == ProgressStatus.Unknown);
        Summary = string.Join(" · ", new[]
        {
            late > 0 ? L.F("x_en_retard", late) : null,
            tight > 0 ? L.F("x_juste_s", tight) : null,
            unknown > 0 ? L.F("x_sans_position", unknown) : null,
        }.OfType<string>().DefaultIfEmpty(L.T("toutes_les_equipes_sont_dans_les_temps")));
        RefreshItems();
        RefreshMarkers();
        RefreshRadioPlan();
        RefreshPlayers();
        PublishedTeamNames = Statuses.Select(s => s.Team.Name).ToList();
        PublishedPositions = Statuses
            .Select(s => (s.Team.Name, Position: _positions.LastOrDefault(p => p.TeamId == s.Team.Model.Id)))
            .Where(x => x.Position is not null)
            .Select(x => new Services.Gps.PublishedPosition(x.Name, x.Position!.Latitude, x.Position.Longitude, x.Position.ReceivedAt, x.Position.Source))
            .ToList();
    }

    /// <summary>« Orga : PMR 8 · Urgence : 06 ... » affiché en tête du plan radio.</summary>
    [ObservableProperty]
    private string _orgaContact = "";

    private RadioCheckViewModel? _radioCheck;

    /// <summary>Vérification des doublons : les fréquences en double sont signalées dans le plan radio.</summary>
    public RadioCheckViewModel? RadioCheck
    {
        get => _radioCheck;
        set
        {
            _radioCheck = value;
            if (value is not null)
                value.Changed += RefreshRadioPlan;
            RefreshRadioPlan();
        }
    }

    /// <summary>Plan radio replié (quelques lignes, défilement) ou déplié (toutes les équipes).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RadioPlanMaxHeight))]
    private bool _isRadioPlanExpanded;

    public double RadioPlanMaxHeight => IsRadioPlanExpanded ? double.PositiveInfinity : 68;

    public void RefreshRadioPlan()
    {
        var duplicates = RadioCheck?.ConflictingKeys ?? new HashSet<string>();
        static string Frequency(string value) => value.Length > 0 ? value : "—";
        var operation = _file.Operation;
        OrgaContact = string.Join(" · ", new[]
        {
            operation.OrgaRadioFrequency.Length > 0 ? L.F("orga_x", operation.OrgaRadioFrequency) : null,
            operation.EmergencyPhone.Length > 0 ? L.F("urgence_x_2", operation.EmergencyPhone) : null,
        }.OfType<string>());
        RadioPlan = Missions.Columns
            .GroupBy(t => t.Faction)
            .Select(g => new RadioFaction(
                g.Key?.Name ?? L.T("sans_faction"),
                g.Key?.Color ?? "#607D8B",
                g.Key is null ? "" : Frequency(g.Key.RadioFrequency),
                g.Select(t => new RadioTeam(t.Name, Frequency(t.RadioFrequency), g.Key?.CommandTeam == t,
                    duplicates.Contains(RadioCheckViewModel.KeyOf(t)))).ToList(),
                g.Key is not null && duplicates.Contains(RadioCheckViewModel.KeyOf(g.Key))))
            .ToList();
    }

    private void RefreshMarkers()
    {
        Markers = Statuses
            .Where(s => s.Position is not null)
            .Select(s => new TeamMarker(
                s.Position!.Value,
                s.Team.Faction?.Color ?? "#607D8B",
                s.Team.Name,
                s.StatusColor,
                s.Status is ProgressStatus.Late or ProgressStatus.Tight ? s.Target : null,
                s == Selected,
                s.Team.ResolvedSymbol,
                s.Team.Echelon))
            .ToList();

        // Téléphones des orgas : dernière position avant l'instant suivi, point blanc « ★ nom (rôle) ».
        var orgaNow = new DateTimeOffset(_operation.ToDateTime(NowMinutes));
        Markers = [.. Markers, .. (Organizers?.Items ?? [])
            .Select(o => (Organizer: o, Position: _positions.LastOrDefault(p => p.TeamId == o.Model.Id && p.ReceivedAt <= orgaNow.AddSeconds(1))))
            .Where(x => x.Position is not null)
            .Select(x => new TeamMarker(x.Position!.Point, "#ECEFF1", $"★ {x.Organizer.Name}{(x.Organizer.Role.Length > 0 ? $" ({x.Organizer.Role})" : "")}",
                "#FFFFFF", null, false, AirsoftPlanner.Core.Symbols.MilSymbol.Dot))];

        var nowDate = new DateTimeOffset(_operation.ToDateTime(NowMinutes));
        var vehicleMarkers = Vehicles is null ? [] : _teams.Items
            .SelectMany(t => t.Vehicles.Where(v => v.InGame).Select(v => (Team: t, Vehicle: v)))
            .Select(x => (x.Team, x.Vehicle, Point: Vehicles.LastPosition(x.Vehicle.Model.Id, nowDate)))
            .Where(x => x.Point is not null)
            .Select(x => new ItemMarker(x.Point!.Value, $"{x.Team.Name} {x.Vehicle.Kind}", false, x.Team.Faction?.Color ?? "#607D8B"));
        ItemMarkers = TrackedItems
            .Where(t => t.Location is not null && t.StateColor != "#2E7D32")
            .Select(t => new ItemMarker(t.Location!.Value, $"📦 {t.Item.Name}", t == SelectedItem))
            .Concat(vehicleMarkers)
            .ToList();

        var now = NowMinutes;
        Trails = !ShowTrails ? [] : Statuses
            .Select(s => new TeamTrail(
                s.Team.Faction?.Color ?? "#607D8B",
                _positions.Where(p => p.TeamId == s.Team.Model.Id && _operation.ToMinutes(p.ReceivedAt.LocalDateTime) <= now + 0.01)
                    .Select(p => p.Point).ToList()))
            .Where(t => t.Points.Count >= 2)
            .ToList();
    }

    private string DescribeMission(Mission mission)
    {
        var zone = _terrain.Zones.FirstOrDefault(z => z.Model.Id == mission.ZoneId)?.Name;
        return $"{mission.Name} à {MissionTime.Format(mission.StartMinutes)}{(zone is null ? "" : $" — {zone}")}";
    }

    private void RebuildStatuses()
    {
        var selectedTeam = Selected?.Team;
        Statuses.Clear();
        foreach (var team in Missions.Columns)
            Statuses.Add(new TeamStatusViewModel(team));
        Selected = Statuses.FirstOrDefault(s => s.Team == selectedTeam);
        Refresh();
    }

    private void OnOperationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OperationViewModel.StartMinutes) or nameof(OperationViewModel.EndMinutes))
        {
            OnPropertyChanged(nameof(SimulationStart));
            OnPropertyChanged(nameof(SimulationEnd));
            SimulatedMinutes = Math.Clamp(SimulatedMinutes, SimulationStart, SimulationEnd);
        }

        if (e.PropertyName is nameof(OperationViewModel.WalkingSpeedKmh) or nameof(OperationViewModel.VehicleSpeedKmh) or nameof(OperationViewModel.CoordinateFormat)
            or nameof(OperationViewModel.StartMinutes))
            Refresh();
    }

    private void OnZonesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var zone in e.NewItems?.OfType<ZoneViewModel>() ?? [])
            zone.PropertyChanged += OnZoneChanged;
        foreach (var zone in e.OldItems?.OfType<ZoneViewModel>() ?? [])
            zone.PropertyChanged -= OnZoneChanged;
    }

    private void OnZoneChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ZoneViewModel.Points) or nameof(ZoneViewModel.Name))
            Refresh();
    }
}
