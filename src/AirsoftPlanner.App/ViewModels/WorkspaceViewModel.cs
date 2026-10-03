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
        Teams = new TeamsViewModel(file, Factions);
        Terrain = new TerrainViewModel(file, General, dialogs);
        Missions = new MissionsViewModel(file, General, Factions, Teams, Terrain);
    }

    public OperationViewModel General { get; }

    public FactionsViewModel Factions { get; }

    public TeamsViewModel Teams { get; }

    public TerrainViewModel Terrain { get; }

    public MissionsViewModel Missions { get; }
}
