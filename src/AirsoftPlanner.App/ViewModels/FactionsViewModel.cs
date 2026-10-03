using System;
using System.Collections.ObjectModel;
using System.Linq;
using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AirsoftPlanner.App.ViewModels;

public partial class FactionsViewModel : ViewModelBase
{
    private readonly OperationFile _file;

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
        Items.Add(viewModel);
        Selected = viewModel;
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
