namespace AirsoftPlanner.Core.Domain;

/// <summary>Véhicules amenés par une équipe (une ligne par type).</summary>
public class TeamVehicle : Entity
{
    public Guid TeamId { get; set; }

    /// <summary>Type de véhicule (4x4, quad, pick-up, camion...).</summary>
    public string Kind { get; set; } = "";

    public int Quantity { get; set; } = 1;

    public string Notes { get; set; } = "";

    public int SortOrder { get; set; }
}
