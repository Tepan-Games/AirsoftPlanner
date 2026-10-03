using AirsoftPlanner.Core.Domain;

namespace AirsoftPlanner.App.ViewModels;

public class FactionViewModel(Faction faction) : ViewModelBase
{
    public Faction Model => faction;

    public string Name
    {
        get => faction.Name;
        set => SetProperty(faction.Name, value, faction, (f, v) => f.Name = v);
    }

    public string Color
    {
        get => faction.Color;
        set => SetProperty(faction.Color, value, faction, (f, v) => f.Color = v);
    }

    public string Description
    {
        get => faction.Description;
        set => SetProperty(faction.Description, value, faction, (f, v) => f.Description = v);
    }
}
