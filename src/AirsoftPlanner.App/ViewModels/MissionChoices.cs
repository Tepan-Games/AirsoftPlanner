using System.Linq;

namespace AirsoftPlanner.App.ViewModels;

/// <summary>Case à cocher « cette équipe participe à la mission ».</summary>
public class TeamChoiceViewModel(TeamViewModel team, MissionViewModel mission) : ViewModelBase
{
    public TeamViewModel Team => team;

    public bool IsSelected
    {
        get => mission.TeamIds.Contains(team.Model.Id);
        set
        {
            mission.SetTeam(team.Model.Id, value);
            OnPropertyChanged();
        }
    }
}

/// <summary>Case à cocher « cette mission doit être terminée avant ». Désactivée si elle créerait une boucle.</summary>
public class PredecessorChoiceViewModel(MissionViewModel candidate, MissionViewModel mission, bool wouldCreateCycle) : ViewModelBase
{
    public MissionViewModel Mission => candidate;

    public bool CanSelect => IsSelected || !wouldCreateCycle;

    public bool IsSelected
    {
        get => mission.PredecessorIds.Contains(candidate.Model.Id);
        set
        {
            mission.SetPredecessor(candidate.Model.Id, value);
            OnPropertyChanged();
        }
    }
}

/// <summary>Case de la frise désignée par un double-clic : une équipe et une heure.</summary>
public record TimelineSlot(TeamViewModel Team, int Minutes);

/// <summary>Ligne « matériel utilisé » d'une mission, avec sa quantité modifiable.</summary>
public class MissionItemUseViewModel(GameItemViewModel item, int quantity, MissionViewModel mission) : ViewModelBase
{
    public GameItemViewModel Item => item;

    public decimal? Quantity
    {
        get => quantity;
        set => mission.SetItemQuantity(item.Model.Id, System.Math.Max(1, (int)(value ?? 1)));
    }
}
