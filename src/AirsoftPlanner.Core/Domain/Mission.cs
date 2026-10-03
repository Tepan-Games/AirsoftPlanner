namespace AirsoftPlanner.Core.Domain;

/// <summary>Une mission du scénario, confiée à une ou plusieurs équipes, dans une zone et sur un créneau.</summary>
public class Mission : Entity
{
    public string Name { get; set; } = "";

    /// <summary>Briefing : objectif, consignes, conditions de réussite (repris dans les ordres de mission).</summary>
    public string Description { get; set; } = "";

    public Guid? ZoneId { get; set; }

    public List<Guid> TeamIds { get; set; } = [];

    /// <summary>Début en minutes depuis minuit le jour de l'OP (peut dépasser 24 h pour une OP de nuit).</summary>
    public int StartMinutes { get; set; }

    public int DurationMinutes { get; set; } = 30;

    public int EndMinutes => StartMinutes + DurationMinutes;

    /// <summary>Une mission non essentielle peut être désactivée pour rattraper un retard.</summary>
    public bool IsEssential { get; set; } = true;

    public bool IsEnabled { get; set; } = true;

    /// <summary>Missions qui doivent être terminées avant que celle-ci commence.</summary>
    public List<Guid> PredecessorIds { get; set; } = [];
}
