using System.Collections.Generic;
using System.Linq;
using AirsoftPlanner.Core.Domain;

namespace AirsoftPlanner.App.ViewModels;

public class TeamViewModel(Team team, IReadOnlyCollection<FactionViewModel> factions) : ViewModelBase
{
    public Team Model => team;

    public string Name
    {
        get => team.Name;
        set => SetProperty(team.Name, value, team, (t, v) => t.Name = v);
    }

    public FactionViewModel? Faction
    {
        get => factions.FirstOrDefault(f => f.Model.Id == team.FactionId);
        set
        {
            if (value?.Model.Id == team.FactionId)
                return;

            team.FactionId = value?.Model.Id;
            OnPropertyChanged();
        }
    }

    public string LeaderName
    {
        get => team.LeaderName;
        set => SetProperty(team.LeaderName, value, team, (t, v) => t.LeaderName = v);
    }

    public string LeaderPhone
    {
        get => team.LeaderPhone;
        set => SetProperty(team.LeaderPhone, value, team, (t, v) => t.LeaderPhone = v);
    }

    public decimal? PlayerCount
    {
        get => team.PlayerCount;
        set => SetProperty(team.PlayerCount, (int)(value ?? 0), team, (t, v) => t.PlayerCount = v);
    }

    public string Notes
    {
        get => team.Notes;
        set => SetProperty(team.Notes, value, team, (t, v) => t.Notes = v);
    }

    /// <summary>À appeler quand la liste des factions ou le nom/la couleur de la faction change.</summary>
    public void RefreshFaction() => OnPropertyChanged(nameof(Faction));
}
