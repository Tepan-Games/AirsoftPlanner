using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AirsoftPlanner.App.ViewModels;

public record GameItemCategoryOption(GameItemCategory Value, string Label, bool ConsumableByDefault)
{
    public static IReadOnlyList<GameItemCategoryOption> All { get; } =
    [
        new(GameItemCategory.Crate, "Caisse", false),
        new(GameItemCategory.Pyrotechnic, "Artifice / grenade", true),
        new(GameItemCategory.Smoke, "Fumigène", true),
        new(GameItemCategory.Prop, "Accessoire de décor", false),
        new(GameItemCategory.Document, "Document / renseignement", false),
        new(GameItemCategory.Communication, "Radio / électronique", false),
        new(GameItemCategory.Other, "Autre", false),
    ];

    public static GameItemCategoryOption Of(GameItemCategory category) => All.First(o => o.Value == category);

    public override string ToString() => Label;
}

/// <summary>Utilisation d'un élément de jeu par une mission.</summary>
public record GameItemUsage(string MissionName, string TimeText, int Quantity, bool IsEnabled);

public class GameItemViewModel(GameItem item) : ViewModelBase
{
    private IReadOnlyList<GameItemUsage> _usages = [];

    public GameItem Model => item;

    public string Name
    {
        get => item.Name;
        set => SetProperty(item.Name, value, item, (i, v) => i.Name = v);
    }

    public GameItemCategoryOption Category
    {
        get => GameItemCategoryOption.Of(item.Category);
        set
        {
            if (value is null || value.Value == item.Category)
                return;

            item.Category = value.Value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CategoryLabel));
            IsConsumable = value.ConsumableByDefault;
        }
    }

    public string CategoryLabel => Category.Label;

    public decimal? Quantity
    {
        get => item.Quantity;
        set
        {
            if (SetProperty(item.Quantity, Math.Max(0, (int)(value ?? 0)), item, (i, v) => i.Quantity = v))
                RefreshUsage();
        }
    }

    public bool IsConsumable
    {
        get => item.IsConsumable;
        set
        {
            if (SetProperty(item.IsConsumable, value, item, (i, v) => i.IsConsumable = v))
                RefreshUsage();
        }
    }

    public string Description
    {
        get => item.Description;
        set => SetProperty(item.Description, value, item, (i, v) => i.Description = v);
    }

    /// <summary>Objet d'objectif suivi pendant l'OP (détenteur, position), pour le récupérer sur le terrain.</summary>
    public bool IsTracked
    {
        get => item.IsTracked;
        set => SetProperty(item.IsTracked, value, item, (i, v) => i.IsTracked = v);
    }

    public IReadOnlyList<GameItemUsage> Usages
    {
        get => _usages;
        set
        {
            _usages = value;
            OnPropertyChanged();
            RefreshUsage();
        }
    }

    public bool IsUsed => _usages.Any(u => u.IsEnabled);

    public int UsedQuantity => _usages.Where(u => u.IsEnabled).Sum(u => u.Quantity);

    /// <summary>Signalé par l'analyse du planning (stock insuffisant au total ou en simultané).</summary>
    public bool HasShortage { get; private set; }

    public string UsageSummary => !IsUsed
        ? "Utilisé dans aucune mission"
        : item.IsConsumable
            ? $"{UsedQuantity} / {item.Quantity} consommé(s) dans {_usages.Count(u => u.IsEnabled)} mission(s)"
            : $"Utilisé dans {_usages.Count(u => u.IsEnabled)} mission(s) · stock {item.Quantity}";

    public void SetShortage(bool shortage)
    {
        if (shortage == HasShortage)
            return;

        HasShortage = shortage;
        OnPropertyChanged(nameof(HasShortage));
    }

    private void RefreshUsage()
    {
        OnPropertyChanged(nameof(IsUsed));
        OnPropertyChanged(nameof(UsedQuantity));
        OnPropertyChanged(nameof(UsageSummary));
    }
}

/// <summary>Inventaire du matériel de jeu de l'OP.</summary>
public partial class GameItemsViewModel : ViewModelBase
{
    private readonly OperationFile _file;

    public GameItemsViewModel(OperationFile file)
    {
        _file = file;
        Items = new ObservableCollection<GameItemViewModel>(file.LoadGameItems().Select(i => new GameItemViewModel(i)));
        Selected = Items.FirstOrDefault();
    }

    /// <summary>Déclenché quand un élément est supprimé, pour le retirer des missions.</summary>
    public event Action<GameItemViewModel>? Removed;

    public ObservableCollection<GameItemViewModel> Items { get; }

    public IReadOnlyList<GameItemCategoryOption> Categories => GameItemCategoryOption.All;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    private GameItemViewModel? _selected;

    public bool HasSelection => Selected is not null;

    [RelayCommand]
    private void Add()
    {
        var item = new GameItem { Name = $"Élément {Items.Count + 1}", Category = GameItemCategory.Crate };
        _file.Add(item);
        var viewModel = new GameItemViewModel(item);
        Items.Add(viewModel);
        Selected = viewModel;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Remove()
    {
        var item = Selected!;
        var index = Items.IndexOf(item);
        _file.Remove(item.Model);
        Items.Remove(item);
        Removed?.Invoke(item);
        Selected = Items.Count == 0 ? null : Items[Math.Min(index, Items.Count - 1)];
    }
}
