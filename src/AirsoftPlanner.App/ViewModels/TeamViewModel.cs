using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Core.Registration;
using AirsoftPlanner.Core.Localization;

namespace AirsoftPlanner.App.ViewModels;

public class TeamViewModel : ViewModelBase
{
    private readonly Team _team;
    private readonly IReadOnlyCollection<FactionViewModel> _factions;

    public TeamViewModel(Team team, IReadOnlyCollection<FactionViewModel> factions,
        IEnumerable<TeamMember> members, IEnumerable<TeamVehicle> vehicles, VehicleTracker? tracker = null)
    {
        Tracker = tracker;
        _team = team;
        _factions = factions;
        Members = new ObservableCollection<MemberViewModel>(members.Select(m => new MemberViewModel(m, OnLeaderChanged)));
        Vehicles = new ObservableCollection<VehicleViewModel>(vehicles.Select(v => new VehicleViewModel(v, tracker)));
        Members.CollectionChanged += (_, e) =>
        {
            foreach (var member in e.NewItems?.OfType<MemberViewModel>() ?? [])
                member.PropertyChanged += OnMemberChanged;
            OnMembersChanged();
        };
        foreach (var member in Members)
            member.PropertyChanged += OnMemberChanged;
        Vehicles.CollectionChanged += (_, e) =>
        {
            foreach (var vehicle in e.NewItems?.OfType<VehicleViewModel>() ?? [])
                vehicle.PropertyChanged += OnVehicleChanged;
            OnPropertyChanged(nameof(VehicleSummary));
        };
        foreach (var vehicle in Vehicles)
            vehicle.PropertyChanged += OnVehicleChanged;
    }

    public Team Model => _team;

    public VehicleTracker? Tracker { get; }

    public ObservableCollection<MemberViewModel> Members { get; }

    public ObservableCollection<VehicleViewModel> Vehicles { get; }

    public string Name
    {
        get => _team.Name;
        set => SetProperty(_team.Name, value, _team, (t, v) => t.Name = v);
    }

    public FactionViewModel? Faction
    {
        get => _factions.FirstOrDefault(f => f.Model.Id == _team.FactionId);
        set
        {
            if (value?.Model.Id == _team.FactionId)
                return;

            _team.FactionId = value?.Model.Id;
            OnPropertyChanged();
        }
    }

    /// <summary>Symbole de l'équipe sur les cartes.</summary>
    public MilSymbolOption Symbol
    {
        get => MilSymbolOption.All.First(o => o.Value == _team.Symbol);
        set
        {
            if (value is null || value.Value == _team.Symbol)
                return;
            _team.Symbol = value.Value;
            OnPropertyChanged();
        }
    }

    /// <summary>Symbole dessiné : Auto = QG pour l'équipe de commandement de la faction, infanterie sinon.</summary>
    public AirsoftPlanner.Core.Symbols.MilSymbol ResolvedSymbol => _team.Symbol != AirsoftPlanner.Core.Symbols.MilSymbol.Auto
        ? _team.Symbol
        : Faction?.CommandTeam == this ? AirsoftPlanner.Core.Symbols.MilSymbol.Headquarters : AirsoftPlanner.Core.Symbols.MilSymbol.Infantry;

    /// <summary>Indicateur de taille déduit de l'effectif.</summary>
    public AirsoftPlanner.Core.Symbols.Echelon Echelon => AirsoftPlanner.Core.Symbols.MilitarySymbols.EchelonForSize(Size);

    public string RadioFrequency
    {
        get => _team.RadioFrequency;
        set
        {
            if (SetProperty(_team.RadioFrequency, value, _team, (t, v) => t.RadioFrequency = v))
                OnPropertyChanged(nameof(RadioFrequencyLabel));
        }
    }

    /// <summary>Identifiants des appareils GPS de l'équipe (Traccar Client, nœud Meshtastic...), séparés par des virgules.</summary>
    public string GpsDeviceIds
    {
        get => _team.GpsDeviceIds;
        set => SetProperty(_team.GpsDeviceIds, value, _team, (t, v) => t.GpsDeviceIds = v);
    }

    public string EnrollmentCode
    {
        get => _team.EnrollmentCode;
        set
        {
            if (SetProperty(_team.EnrollmentCode, value, _team, (t, v) => t.EnrollmentCode = v))
                OnPropertyChanged(nameof(EnrollmentCodeText));
        }
    }

    public string EnrollmentCodeText => _team.EnrollmentCode.Length == 0 ? "—" : Core.Gps.EnrollmentCodes.Format(_team.EnrollmentCode);

    public string RadioFrequencyLabel => _team.RadioFrequency.Length > 0 ? $"📻 {_team.RadioFrequency}" : L.T("frequence_non_definie");

    /// <summary>Effectif annoncé à l'inscription (les membres peuvent n'être saisis qu'en partie).</summary>
    public decimal? PlayerCount
    {
        get => _team.PlayerCount;
        set
        {
            if (!SetProperty(_team.PlayerCount, (int)(value ?? 0), _team, (t, v) => t.PlayerCount = v))
                return;
            OnPropertyChanged(nameof(Size));
            OnPropertyChanged(nameof(SizeText));
        }
    }

    public bool HasMembers => Members.Count > 0;

    /// <summary>Effectif retenu : le plus grand entre les membres saisis et l'effectif annoncé.</summary>
    public int Size => System.Math.Max(Members.Count, _team.PlayerCount);

    public string SizeText => Members.Count switch
    {
        0 => L.F("x_joueur_s_annonce_s", _team.PlayerCount),
        var count when count < _team.PlayerCount => L.F("x_joueur_s_annonce_s_x_renseigne_s", _team.PlayerCount, count),
        var count => L.F("x_joueur_s", count),
    };

    public MemberViewModel? Leader => Members.FirstOrDefault(m => m.IsLeader);

    public string LeaderText => Leader is { } leader
        ? $"{leader.DisplayName}{(leader.Phone.Length > 0 ? " · " + leader.Phone : "")}"
        : L.T("pas_de_chef_d_equipe_designe");

    public string VehicleSummary => Vehicles.Count == 0
        ? L.T("pas_de_vehicule")
        : string.Join(", ", Vehicles.Select(v => $"{v.Quantity} × {(v.Kind.Length > 0 ? v.Kind : L.T("vehicule_2"))}"));

    public string Notes
    {
        get => _team.Notes;
        set => SetProperty(_team.Notes, value, _team, (t, v) => t.Notes = v);
    }

    public RegistrationStatusOption Status
    {
        get => RegistrationStatusOption.Of(_team.Status);
        set
        {
            if (value is null || value.Value == _team.Status)
                return;

            _team.Status = value.Value;
            _team.RegisteredAt ??= System.DateTimeOffset.Now;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsPlaying));
        }
    }

    /// <summary>Pré-inscrite ou confirmée : comptée dans les effectifs et présente sur la frise.</summary>
    public bool IsPlaying => RegistrationRules.IsPlaying(_team.Status);

    public System.DateTime? RegisteredAt
    {
        get => _team.RegisteredAt?.LocalDateTime.Date;
        set
        {
            var date = value is { } d ? new System.DateTimeOffset(d.Date) : (System.DateTimeOffset?)null;
            if (date == _team.RegisteredAt)
                return;

            _team.RegisteredAt = date;
            OnPropertyChanged();
        }
    }

    /// <summary>Ajoute un membre en appliquant la règle « un seul chef par équipe ».</summary>
    public MemberViewModel AddMember(TeamMember member)
    {
        var viewModel = new MemberViewModel(member, OnLeaderChanged);
        Members.Add(viewModel);
        return viewModel;
    }

    private void OnLeaderChanged(MemberViewModel leader)
    {
        foreach (var other in Members.Where(m => m != leader && m.IsLeader))
            other.IsLeader = false;
        OnPropertyChanged(nameof(Leader));
        OnPropertyChanged(nameof(LeaderText));
    }

    private void OnMemberChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MemberViewModel.IsLeader) or nameof(MemberViewModel.FirstName)
            or nameof(MemberViewModel.LastName) or nameof(MemberViewModel.Callsign) or nameof(MemberViewModel.Phone))
        {
            OnPropertyChanged(nameof(Leader));
            OnPropertyChanged(nameof(LeaderText));
        }
    }

    private void OnVehicleChanged(object? sender, PropertyChangedEventArgs e) => OnPropertyChanged(nameof(VehicleSummary));

    private void OnMembersChanged()
    {
        OnPropertyChanged(nameof(HasMembers));
        OnPropertyChanged(nameof(Size));
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(Leader));
        OnPropertyChanged(nameof(LeaderText));
    }
}
