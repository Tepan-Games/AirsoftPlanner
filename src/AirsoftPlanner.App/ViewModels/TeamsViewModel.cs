using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AirsoftPlanner.App.Services;
using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Core.Registration;
using AirsoftPlanner.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AirsoftPlanner.App.ViewModels;

public partial class TeamsViewModel : ViewModelBase
{
    private readonly OperationFile _file;
    private readonly IFileDialogService _dialogs;

    private readonly VehicleTracker _tracker;

    public TeamsViewModel(OperationFile file, FactionsViewModel factions, IFileDialogService dialogs, VehicleTracker tracker)
    {
        _file = file;
        _dialogs = dialogs;
        _tracker = tracker;
        Factions = factions.Items;
        var members = file.LoadMembers().ToLookup(m => m.TeamId);
        var vehicles = file.LoadVehicles().ToLookup(v => v.TeamId);
        Items = new ObservableCollection<TeamViewModel>(file.LoadTeams()
            .Select(t => new TeamViewModel(t, Factions, members[t.Id], vehicles[t.Id], tracker)));
        tracker.Changed += () =>
        {
            foreach (var vehicle in Items.SelectMany(t => t.Vehicles))
                vehicle.RefreshKilometers();
        };
        Selected = Items.FirstOrDefault();
        factions.Removed += OnFactionRemoved;
        foreach (var team in Items)
            team.PropertyChanged += OnTeamChanged;
        Items.CollectionChanged += (_, e) =>
        {
            foreach (var team in e.NewItems?.OfType<TeamViewModel>() ?? [])
                team.PropertyChanged += OnTeamChanged;
            RefreshSummary();
        };
    }

    private void OnTeamChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TeamViewModel.Status) or nameof(TeamViewModel.Size))
            RefreshSummary();
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

    public IReadOnlyList<RegistrationStatusOption> Statuses => RegistrationStatusOption.All;

    /// <summary>Bilan des inscriptions par statut.</summary>
    public string Summary
    {
        get
        {
            var playing = Items.Where(t => t.IsPlaying).ToList();
            var parts = RegistrationStatusOption.All
                .Select(o => (o.Label, Count: Items.Count(t => t.Status.Value == o.Value)))
                .Where(x => x.Count > 0)
                .Select(x => $"{x.Count} {x.Label.ToLowerInvariant()}");
            return $"{playing.Sum(t => t.Size)} joueur(s) inscrit(s) · " + string.Join(" · ", parts);
        }
    }

    public void RefreshSummary() => OnPropertyChanged(nameof(Summary));

    /// <summary>
    /// Importe des inscriptions depuis un CSV (export de formulaire ou de tableur) : une équipe pré-inscrite par ligne,
    /// avec son contact comme chef d'équipe ; si la faction demandée est complète, l'équipe va en liste d'attente.
    /// </summary>
    [RelayCommand]
    private async Task ImportCsvAsync()
    {
        var path = await _dialogs.PickOpenFileAsync("Importer des inscriptions", "Fichier CSV", ["*.csv", "*.txt"]);
        if (path is null)
            return;

        IReadOnlyList<RegistrationRow> rows;
        try
        {
            rows = RegistrationCsv.Parse(await File.ReadAllTextAsync(path));
        }
        catch (Exception ex) when (ex is IOException or FormatException)
        {
            await _dialogs.ShowErrorAsync($"Import impossible : {ex.Message}");
            return;
        }

        int created = 0, skipped = 0, waiting = 0;
        foreach (var row in rows)
        {
            if (Items.Any(t => string.Equals(t.Name.Trim(), row.TeamName, StringComparison.CurrentCultureIgnoreCase)))
            {
                skipped++;
                continue;
            }

            var faction = Factions.FirstOrDefault(f => row.Faction.Length > 0
                && (f.Name.Contains(row.Faction, StringComparison.CurrentCultureIgnoreCase)
                    || row.Faction.Contains(f.Name, StringComparison.CurrentCultureIgnoreCase)));
            var full = faction is { Model.MaxPlayers: > 0 } && faction.PlayerCount + row.Players > faction.Model.MaxPlayers;
            var team = new Team
            {
                Name = row.TeamName,
                FactionId = faction?.Model.Id,
                PlayerCount = row.Players,
                Notes = row.Notes,
                Status = full ? RegistrationStatus.WaitingList : RegistrationStatus.PreRegistered,
                RegisteredAt = DateTimeOffset.Now,
            };
            _file.Add(team);
            var viewModel = new TeamViewModel(team, Factions, [], [], _tracker);
            Items.Add(viewModel);
            if (row.ContactName.Length > 0 || row.Phone.Length > 0 || row.Email.Length > 0)
            {
                var parts = row.ContactName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                var leader = new TeamMember
                {
                    TeamId = team.Id,
                    FirstName = parts.ElementAtOrDefault(0) ?? "",
                    LastName = parts.ElementAtOrDefault(1) ?? "",
                    Phone = row.Phone,
                    Email = row.Email,
                    IsLeader = true,
                };
                _file.Add(leader);
                viewModel.AddMember(leader);
            }

            created++;
            if (full)
                waiting++;
        }

        RefreshSummary();
        await _dialogs.ShowInfoAsync("Import des inscriptions",
            $"{created} équipe(s) importée(s) en pré-inscription"
            + (waiting > 0 ? $", dont {waiting} en liste d'attente (faction complète)" : "")
            + (skipped > 0 ? $". {skipped} ligne(s) ignorée(s) : équipe déjà présente." : "."));
    }

    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        var path = await _dialogs.PickSaveFileAsync("Exporter les inscriptions", "inscriptions.csv", "Fichier CSV", ".csv");
        if (path is null)
            return;

        var rows = Items.Select(t => (
            new RegistrationRow(t.Name, t.Faction?.Name ?? "", t.Leader?.DisplayName ?? "", t.Leader?.Phone ?? "",
                t.Leader?.Email ?? "", t.Size, t.Notes),
            t.Status.Label,
            t.Model.RegisteredAt));
        // BOM UTF-8 : Excel affiche correctement les accents.
        await File.WriteAllTextAsync(path, RegistrationCsv.Write(rows), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    /// <summary>Fait entrer les équipes en attente là où des places se sont libérées (premier inscrit, premier servi).</summary>
    [RelayCommand]
    private async Task PromoteWaitingAsync()
    {
        var promoted = new List<string>();
        foreach (var group in Items.Where(t => t.Status.Value == RegistrationStatus.WaitingList).GroupBy(t => t.Faction))
        {
            var max = group.Key?.Model.MaxPlayers ?? 0;
            var current = group.Key?.PlayerCount ?? 0;
            var ids = RegistrationRules.Promotable(max, current,
                group.Select(t => (t.Model.Id, t.Size, t.Model.RegisteredAt ?? DateTimeOffset.MaxValue)));
            foreach (var team in group.Where(t => ids.Contains(t.Model.Id)))
            {
                team.Status = RegistrationStatusOption.Of(RegistrationStatus.PreRegistered);
                promoted.Add(team.Name);
            }
        }

        RefreshSummary();
        await _dialogs.ShowInfoAsync("Liste d'attente", promoted.Count == 0
            ? "Aucune place libre pour les équipes en attente."
            : $"Équipes passées en pré-inscription : {string.Join(", ", promoted)}.");
    }

    [RelayCommand]
    private void Add()
    {
        var team = new Team { Name = $"Équipe {Items.Count + 1}", RegisteredAt = DateTimeOffset.Now };
        _file.Add(team);
        var viewModel = new TeamViewModel(team, Factions, [], [], _tracker);
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
        var viewModel = new VehicleViewModel(vehicle, _tracker);
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
