namespace AirsoftPlanner.Core.Domain;

/// <summary>Emprise du terrain : rectangle GPS utilisé pour télécharger les fonds de carte et cadrer la vue.</summary>
public class TerrainMap : Entity
{
    public double North { get; set; }

    public double South { get; set; }

    public double West { get; set; }

    public double East { get; set; }

    public GeoBounds Bounds
    {
        get => new(North, South, West, East);
        set => (North, South, West, East) = (value.North, value.South, value.West, value.East);
    }
}
