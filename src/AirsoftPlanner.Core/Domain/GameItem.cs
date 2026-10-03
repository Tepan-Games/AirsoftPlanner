namespace AirsoftPlanner.Core.Domain;

public enum GameItemCategory
{
    Crate,
    Pyrotechnic,
    Smoke,
    Prop,
    Document,
    Communication,
    Other,
}

/// <summary>Élément de jeu disponible pour l'OP : caisse, artifice, fumigène, accessoire...</summary>
public class GameItem : Entity
{
    public string Name { get; set; } = "";

    public GameItemCategory Category { get; set; }

    /// <summary>Quantité disponible pour toute l'OP.</summary>
    public int Quantity { get; set; } = 1;

    /// <summary>Consommé à l'usage (artifice, fumigène) ou réutilisable (caisse, accessoire).</summary>
    public bool IsConsumable { get; set; }

    public string Description { get; set; } = "";
}

/// <summary>Quantité d'un élément de jeu utilisée par une mission.</summary>
public record MissionItemUse(Guid ItemId, int Quantity);
