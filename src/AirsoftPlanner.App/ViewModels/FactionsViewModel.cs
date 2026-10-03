using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AirsoftPlanner.App.ViewModels;

public partial class FactionsViewModel : ViewModelBase
{
    private readonly OperationFile _file;
    private TeamsViewModel? _teams;

    public FactionsViewModel(OperationFile file)
    {
        _file = file;
        Items = new ObservableCollection<FactionViewModel>(file.LoadFactions().Select(f => new FactionViewModel(f)));
        Selected = Items.FirstOrDefault();
    }

    /// <summary>Déclenché quand une faction est supprimée, pour détacher ses équipes.</summary>
    public event Action<FactionViewModel>? Removed;

    public ObservableCollection<FactionViewModel> Items { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    private FactionViewModel? _selected;

    public bool HasSelection => Selected is not null;

    [RelayCommand]
    private void Add()
    {
        var unusedColor = ColorPalette.Colors.FirstOrDefault(c => Items.All(f => f.Color != c)) ?? ColorPalette.Colors[0];
        var faction = new Faction { Name = $"Faction {Items.Count + 1}", Color = unusedColor };
        _file.Add(faction);
        var viewModel = new FactionViewModel(faction);
        if (_teams is { } teams)
            viewModel.AttachTeams(() => teams.Items);
        Items.Add(viewModel);
        Selected = viewModel;
    }

    /// <summary>Relie les factions aux équipes, pour calculer les effectifs et choisir l'équipe chef de faction.</summary>
    public void AttachTeams(TeamsViewModel teams)
    {
        _teams = teams;
        foreach (var faction in Items)
            faction.AttachTeams(() => teams.Items);
        foreach (var team in teams.Items)
            team.PropertyChanged += OnTeamChanged;
        teams.Items.CollectionChanged += OnTeamsChanged;
    }

    private void OnTeamsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var team in e.NewItems?.OfType<TeamViewModel>() ?? [])
            team.PropertyChanged += OnTeamChanged;
        foreach (var team in e.OldItems?.OfType<TeamViewModel>() ?? [])
            team.PropertyChanged -= OnTeamChanged;
        RefreshStaffing();
    }

    private void OnTeamChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TeamViewModel.Size) or nameof(TeamViewModel.Faction) or nameof(TeamViewModel.Name)
            or nameof(TeamViewModel.Status))
            RefreshStaffing();
    }

    private void RefreshStaffing()
    {
        foreach (var faction in Items)
            faction.RefreshStaffing();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Remove()
    {
        var faction = Selected!;
        var index = Items.IndexOf(faction);
        _file.Remove(faction.Model);
        Items.Remove(faction);
        Removed?.Invoke(faction);
        Selected = Items.Count == 0 ? null : Items[Math.Min(index, Items.Count - 1)];
    }
}
