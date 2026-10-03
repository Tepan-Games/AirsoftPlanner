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
        Items = new ObservableCollection<TeamViewModel>(file.LoadTeams().Select(t => new TeamViewModel(t, Factions)));
        Selected = Items.FirstOrDefault();
        factions.Removed += OnFactionRemoved;
    }

    /// <summary>Déclenché quand une équipe est supprimée, pour la retirer des missions.</summary>
    public event Action<TeamViewModel>? Removed;

    public ObservableCollection<FactionViewModel> Factions { get; }

    public ObservableCollection<TeamViewModel> Items { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand), nameof(ClearFactionCommand))]
    private TeamViewModel? _selected;

    public bool HasSelection => Selected is not null;

    [RelayCommand]
    private void Add()
    {
        var team = new Team { Name = $"Équipe {Items.Count + 1}" };
        _file.Add(team);
        var viewModel = new TeamViewModel(team, Factions);
        Items.Add(viewModel);
        Selected = viewModel;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Remove()
    {
        var team = Selected!;
        var index = Items.IndexOf(team);
        _file.Remove(team.Model);
        Items.Remove(team);
        Removed?.Invoke(team);
        Selected = Items.Count == 0 ? null : Items[Math.Min(index, Items.Count - 1)];
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void ClearFaction() => Selected!.Faction = null;

    private void OnFactionRemoved(FactionViewModel faction)
    {
        foreach (var team in Items.Where(t => t.Model.FactionId == faction.Model.Id))
            team.Faction = null;
    }
}
