using System.Collections.ObjectModel;
using System.Linq;
using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AirsoftPlanner.Core.Localization;

namespace AirsoftPlanner.App.ViewModels;

public class OrganizerViewModel(Organizer organizer) : ViewModelBase
{
    public Organizer Model => organizer;

    public string Name
    {
        get => organizer.Name;
        set => SetProperty(organizer.Name, value, organizer, (o, v) => o.Name = v);
    }

    public string Role
    {
        get => organizer.Role;
        set => SetProperty(organizer.Role, value, organizer, (o, v) => o.Role = v);
    }

    public string Phone
    {
        get => organizer.Phone;
        set => SetProperty(organizer.Phone, value, organizer, (o, v) => o.Phone = v);
    }

    public string RadioFrequency
    {
        get => organizer.RadioFrequency;
        set => SetProperty(organizer.RadioFrequency, value, organizer, (o, v) => o.RadioFrequency = v);
    }

    public string Email
    {
        get => organizer.Email;
        set => SetProperty(organizer.Email, value, organizer, (o, v) => o.Email = v);
    }

    public string EnrollmentCode
    {
        get => organizer.EnrollmentCode;
        set
        {
            if (SetProperty(organizer.EnrollmentCode, value, organizer, (o, v) => o.EnrollmentCode = v))
                OnPropertyChanged(nameof(EnrollmentCodeText));
        }
    }

    public string EnrollmentCodeText => organizer.EnrollmentCode.Length == 0 ? "—" : Core.Gps.EnrollmentCodes.Format(organizer.EnrollmentCode);

    public string Notes
    {
        get => organizer.Notes;
        set => SetProperty(organizer.Notes, value, organizer, (o, v) => o.Notes = v);
    }
}

/// <summary>Onglet Orgas : les organisateurs de l'OP, leur rôle et leurs contacts.</summary>
public partial class OrganizersViewModel : ViewModelBase
{
    private readonly OperationFile _file;

    public OrganizersViewModel(OperationFile file, OperationViewModel general)
    {
        _file = file;
        General = general;
        Items = new ObservableCollection<OrganizerViewModel>(file.LoadOrganizers().Select(o => new OrganizerViewModel(o)));
    }

    /// <summary>Nom de l'organisation, fréquence et numéro d'urgence (communs à toute l'orga).</summary>
    public OperationViewModel General { get; }

    public ObservableCollection<OrganizerViewModel> Items { get; }

    /// <summary>Réception GPS (enrôlement du téléphone de l'orga sélectionné).</summary>
    [ObservableProperty]
    private GpsViewModel? _gps;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    private OrganizerViewModel? _selected;

    [RelayCommand]
    private void Add()
    {
        var organizer = new Organizer
        {
            Name = L.T("nouvel_orga"),
            SortOrder = Items.Count == 0 ? 0 : Items.Max(o => o.Model.SortOrder) + 1,
        };
        _file.Add(organizer);
        var viewModel = new OrganizerViewModel(organizer);
        Items.Add(viewModel);
        Selected = viewModel;
    }

    [RelayCommand(CanExecute = nameof(HasSelected))]
    private void Remove()
    {
        _file.Remove(Selected!.Model);
        Items.Remove(Selected);
        Selected = Items.FirstOrDefault();
    }

    private bool HasSelected => Selected is not null;
}
