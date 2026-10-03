namespace AirsoftPlanner.Core.Domain;

/// <summary>
/// Un fond de carte stocké dans le fichier d'OP (photo aérienne IGN, Plan IGN, plan dessiné...),
/// calé sur les coordonnées GPS de ses bords. Fonctionne entièrement hors ligne.
/// </summary>
public class MapLayer : Entity
{
    public string Name { get; set; } = "";

    /// <summary>Provenance, affichée comme crédit sur la carte et les documents (ex. « IGN – Orthophotos »).</summary>
    public string Attribution { get; set; } = "";

    /// <summary>Image PNG ou JPEG, orientée nord en haut.</summary>
    public byte[] Image { get; set; } = [];

    public double North { get; set; }

    public double South { get; set; }

    public double West { get; set; }

    public double East { get; set; }

    public int SortOrder { get; set; }

    public GeoBounds Bounds
    {
        get => new(North, South, West, East);
        set => (North, South, West, East) = (value.North, value.South, value.West, value.East);
    }
}
