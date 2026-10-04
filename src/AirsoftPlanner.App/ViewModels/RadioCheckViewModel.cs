using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Core.Radio;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AirsoftPlanner.Core.Localization;

namespace AirsoftPlanner.App.ViewModels;

/// <summary>
/// Alerte sur les fréquences radio saisies en double (équipes, factions, orga), visible dans tous les onglets.
/// Un doublon normal peut être ignoré ; il revient si une autre équipe rejoint la même fréquence.
/// </summary>
public partial class RadioCheckViewModel : ViewModelBase
{
    private readonly Operation _operation;
    private readonly OperationViewModel _general;
    private readonly FactionsViewModel _factions;
    private readonly TeamsViewModel _teams;
    private readonly OrganizersViewModel _organizers;
    private bool _refreshPending;

    public RadioCheckViewModel(Operation operation, OperationViewModel general, FactionsViewModel factions, TeamsViewModel teams,
        OrganizersViewModel organizers)
    {
        _operation = operation;
        _general = general;
        _factions = factions;
        _teams = teams;
        _organizers = organizers;
        general.PropertyChanged += OnChanged;
        Watch(factions.Items);
        Watch(teams.Items);
        Watch(organizers.Items);
        Refresh();
    }

    public ObservableCollection<RadioConflict> Conflicts { get; } = [];

    /// <summary>Clés (« team:guid », « faction:guid », « orga ») des utilisateurs d'une fréquence en double.</summary>
    public IReadOnlySet<string> ConflictingKeys { get; private set; } = new HashSet<string>();

    [ObservableProperty]
    private bool _hasConflicts;

    /// <summary>Déclenché après chaque nouvelle vérification (plan radio du suivi).</summary>
    public event Action? Changed;

    public bool HasIgnored => _operation.IgnoredRadioConflicts.Count > 0;

    /// <summary>Le doublon est voulu : il n'est plus signalé (tant que personne d'autre ne rejoint la fréquence).</summary>
    [RelayCommand]
    private void Ignore(RadioConflict? conflict)
    {
        if (conflict is null)
            return;
        // Nouvelle liste : la modification est détectée à l'enregistrement.
        _operation.IgnoredRadioConflicts = [.. _operation.IgnoredRadioConflicts, conflict.Signature];
        Refresh();
    }

    /// <summary>Signale de nouveau les doublons ignorés.</summary>
    [RelayCommand]
    private void ShowIgnored()
    {
        _operation.IgnoredRadioConflicts = [];
        Refresh();
    }

    public void Refresh()
    {
        _refreshPending = false;
        var conflicts = RadioPlanCheck.Find(Users(), _operation.IgnoredRadioConflicts);
        Conflicts.Clear();
        foreach (var conflict in conflicts)
            Conflicts.Add(conflict);
        ConflictingKeys = conflicts.SelectMany(c => c.Users.Select(u => u.Key)).ToHashSet();
        HasConflicts = Conflicts.Count > 0;
        OnPropertyChanged(nameof(HasIgnored));
        Changed?.Invoke();
    }

    public static string KeyOf(TeamViewModel team) => $"team:{team.Model.Id}";

    public static string KeyOf(FactionViewModel faction) => $"faction:{faction.Model.Id}";

    private IEnumerable<RadioUser> Users()
    {
        yield return new RadioUser("orga", "orga", _general.OrgaRadioFrequency);
        foreach (var organizer in _organizers.Items)
            yield return new RadioUser("orga", L.F("orga_x_2", organizer.Name), organizer.RadioFrequency);
        foreach (var faction in _factions.Items)
            yield return new RadioUser(KeyOf(faction), $"faction {faction.Name}", faction.RadioFrequency);
        foreach (var team in _teams.Items)
            yield return new RadioUser(KeyOf(team), team.Name, team.RadioFrequency);
    }

    private void Watch<T>(ObservableCollection<T> items) where T : INotifyPropertyChanged
    {
        foreach (var item in items)
            item.PropertyChanged += OnChanged;
        items.CollectionChanged += (_, e) =>
        {
            foreach (var item in e.OldItems?.OfType<T>() ?? [])
                item.PropertyChanged -= OnChanged;
            foreach (var item in e.NewItems?.OfType<T>() ?? [])
                item.PropertyChanged += OnChanged;
            ScheduleRefresh();
        };
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is "RadioFrequency" or "OrgaRadioFrequency" or "Name")
            ScheduleRefresh();
    }

    // Plusieurs modifications d'affilée (saisie, chargement) : une seule vérification.
    private void ScheduleRefresh()
    {
        if (_refreshPending)
            return;
        _refreshPending = true;
        Dispatcher.UIThread.Post(Refresh, DispatcherPriority.Background);
    }
}
