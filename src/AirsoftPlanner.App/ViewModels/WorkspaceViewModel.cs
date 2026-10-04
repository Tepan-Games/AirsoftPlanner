using AirsoftPlanner.App.Services;
using AirsoftPlanner.Data;

namespace AirsoftPlanner.App.ViewModels;

/// <summary>Tous les écrans d'une OP ouverte.</summary>
public class WorkspaceViewModel : ViewModelBase
{
    public WorkspaceViewModel(OperationFile file, IFileDialogService dialogs)
    {
        General = new OperationViewModel(file.Operation);
        Organizers = new OrganizersViewModel(file, General);
        Factions = new FactionsViewModel(file);
        Vehicles = new VehicleTracker(file);
        Teams = new TeamsViewModel(file, Factions, dialogs, Vehicles);
        Factions.AttachTeams(Teams);
        Terrain = new TerrainViewModel(file, General, dialogs);
        Terrain.AttachFactions(Factions);
        GameItems = new GameItemsViewModel(file);
        Missions = new MissionsViewModel(file, General, Factions, Teams, Terrain, GameItems);
        Terrain.AttachMissions(Missions);
        Tracking = new TrackingViewModel(file, General, Teams, Terrain, Missions, GameItems);
        Tracking.Vehicles = Vehicles;
        Tracking.Organizers = Organizers;
        Tracking.Dispatch = new DispatchViewModel(file, General, Teams, Factions, Missions, Terrain, GameItems, dialogs);
        RadioCheck = new RadioCheckViewModel(file.Operation, General, Factions, Teams, Organizers);
        Tracking.RadioCheck = RadioCheck;
        Tracking.Gps = new GpsViewModel(file, Tracking, Teams, dialogs, Vehicles);
        AttachGps(Tracking.Gps);
        Finances = new FinancesViewModel(file, Teams, dialogs, Vehicles);
        Retex = new RetexViewModel(file, General, Teams, Factions, Tracking, GameItems, dialogs);
        Documents = new DocumentsViewModel(file, dialogs, Teams, Factions, Terrain, Missions, GameItems);
    }

    /// <summary>Relie la réception GPS aux onglets Équipes et Orgas (enrôlement), y compris après un rechargement de l'OP.</summary>
    public void AttachGps(GpsViewModel gps)
    {
        gps.Organizers = Organizers;
        gps.Factions = Factions;
        Teams.Gps = gps;
        Organizers.Gps = gps;
        gps.EnrollmentTeam = Teams.Selected;
        gps.EnrollmentOrganizer = Organizers.Selected;
        Teams.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TeamsViewModel.Selected))
                gps.EnrollmentTeam = Teams.Selected;
        };
        Organizers.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OrganizersViewModel.Selected))
                gps.EnrollmentOrganizer = Organizers.Selected;
        };
    }

    public OperationViewModel General { get; }

    public OrganizersViewModel Organizers { get; }

    /// <summary>Fréquences radio en double.</summary>
    public RadioCheckViewModel RadioCheck { get; }

    /// <summary>Traces GPS des véhicules mis en jeu.</summary>
    public VehicleTracker Vehicles { get; }

    public FactionsViewModel Factions { get; }

    public TeamsViewModel Teams { get; }

    public TerrainViewModel Terrain { get; }

    public GameItemsViewModel GameItems { get; }

    public MissionsViewModel Missions { get; }

    public TrackingViewModel Tracking { get; }

    public DocumentsViewModel Documents { get; }

    public FinancesViewModel Finances { get; }

    public RetexViewModel Retex { get; }
}
