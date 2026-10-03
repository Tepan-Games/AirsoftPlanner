using AirsoftPlanner.App.Services;
using AirsoftPlanner.Data;

namespace AirsoftPlanner.App.ViewModels;

/// <summary>Tous les écrans d'une OP ouverte.</summary>
public class WorkspaceViewModel : ViewModelBase
{
    public WorkspaceViewModel(OperationFile file, IFileDialogService dialogs)
    {
        General = new OperationViewModel(file.Operation);
        Factions = new FactionsViewModel(file);
        Teams = new TeamsViewModel(file, Factions, dialogs);
        Factions.AttachTeams(Teams);
        Terrain = new TerrainViewModel(file, General, dialogs);
        GameItems = new GameItemsViewModel(file);
        Missions = new MissionsViewModel(file, General, Factions, Teams, Terrain, GameItems);
        Tracking = new TrackingViewModel(file, General, Teams, Terrain, Missions, GameItems);
        Tracking.Gps = new GpsViewModel(Tracking, Teams, dialogs);
        Finances = new FinancesViewModel(file, Teams, dialogs);
        Documents = new DocumentsViewModel(file, dialogs, Teams, Factions, Terrain, Missions, GameItems);
    }

    public OperationViewModel General { get; }

    public FactionsViewModel Factions { get; }

    public TeamsViewModel Teams { get; }

    public TerrainViewModel Terrain { get; }

    public GameItemsViewModel GameItems { get; }

    public MissionsViewModel Missions { get; }

    public TrackingViewModel Tracking { get; }

    public DocumentsViewModel Documents { get; }

    public FinancesViewModel Finances { get; }
}
