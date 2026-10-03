using System;
using System.Collections.ObjectModel;
using System.Linq;
using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AirsoftPlanner.App.ViewModels;

public partial class TeamsViewModel : ViewModelBase
{
    private readonly OperationFile _file;

    public TeamsViewModel(OperationFile file, FactionsViewModel factions)
    {
        _file = file;
        Factions = factions.Items;
        var members = file.LoadMembers().ToLookup(m => m.TeamId);
        var vehicles = file.LoadVehicles().ToLookup(v => v.TeamId);
        Items = new ObservableCollection<TeamViewModel>(file.LoadTeams()
            .Select(t => new TeamViewModel(t, Factions, members[t.Id], vehicles[t.Id])));
        Selected = Items.FirstOrDefault();
        factions.Removed += OnFactionRemoved;
    }

    /// <summary>Déclenché quand une équipe est supprimée, pour la retirer des missions.</summary>
    public event Action<TeamViewModel>? Removed;

    public ObservableCollection<FactionViewModel> Factions { get; }

    public ObservableCollection<TeamViewModel> Items { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand), nameof(ClearFactionCommand), nameof(AddMemberCommand),
        nameof(AddVehicleCommand))]
    private TeamViewModel? _selected;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveMemberCommand))]
    private MemberViewModel? _selectedMember;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveVehicleCommand))]
    private VehicleViewModel? _selectedVehicle;

    public bool HasSelection => Selected is not null;

    public string Summary => $"{Items.Count} équipe(s) · {Items.Sum(t => t.Size)} joueur(s)";

    [RelayCommand]
    private void Add()
    {
        var team = new Team { Name = $"Équipe {Items.Count + 1}" };
        _file.Add(team);
        var viewModel = new TeamViewModel(team, Factions, [], []);
        Items.Add(viewModel);
        Selected = viewModel;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Remove()
    {
        var team = Selected!;
        var index = Items.IndexOf(team);
        foreach (var member in team.Members)
            _file.Remove(member.Model);
        foreach (var vehicle in team.Vehicles)
            _file.Remove(vehicle.Model);
        _file.Remove(team.Model);
        Items.Remove(team);
        Removed?.Invoke(team);
        Selected = Items.Count == 0 ? null : Items[Math.Min(index, Items.Count - 1)];
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void ClearFaction() => Selected!.Faction = null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void AddMember()
    {
        var team = Selected!;
        var member = new TeamMember
        {
            TeamId = team.Model.Id,
            IsLeader = team.Members.Count == 0,
            SortOrder = team.Members.Count == 0 ? 0 : team.Members.Max(m => m.Model.SortOrder) + 1,
        };
        _file.Add(member);
        SelectedMember = team.AddMember(member);
    }

    [RelayCommand(CanExecute = nameof(CanRemoveMember))]
    private void RemoveMember()
    {
        _file.Remove(SelectedMember!.Model);
        Selected!.Members.Remove(SelectedMember);
        SelectedMember = null;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void AddVehicle()
    {
        var team = Selected!;
        var vehicle = new TeamVehicle
        {
            TeamId = team.Model.Id,
            Kind = "4x4",
            SortOrder = team.Vehicles.Count == 0 ? 0 : team.Vehicles.Max(v => v.Model.SortOrder) + 1,
        };
        _file.Add(vehicle);
        var viewModel = new VehicleViewModel(vehicle);
        team.Vehicles.Add(viewModel);
        SelectedVehicle = viewModel;
    }

    [RelayCommand(CanExecute = nameof(CanRemoveVehicle))]
    private void RemoveVehicle()
    {
        _file.Remove(SelectedVehicle!.Model);
        Selected!.Vehicles.Remove(SelectedVehicle);
        SelectedVehicle = null;
    }

    private bool CanRemoveMember => SelectedMember is not null && Selected is not null;

    private bool CanRemoveVehicle => SelectedVehicle is not null && Selected is not null;

    private void OnFactionRemoved(FactionViewModel faction)
    {
        foreach (var team in Items.Where(t => t.Model.FactionId == faction.Model.Id))
            team.Faction = null;
    }
}
