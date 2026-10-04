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

    /// <summary>Effectif maximum de joueurs sur la mission, ou null sans limite.</summary>
    public int? MaxPlayers { get; set; }

    /// <summary>Matériel de jeu utilisé par la mission.</summary>
    public List<MissionItemUse> Items { get; set; } = [];

    /// <summary>Résultat saisi par l'orga (le même pour toutes les équipes de la mission).</summary>
    public MissionResult Result { get; set; }

    /// <summary>Commentaire sur le résultat (« otage récupéré, documents perdus »...).</summary>
    public string ResultNotes { get; set; } = "";

    /// <summary>Points rapportés à chaque équipe engagée (et à sa faction) si la mission est réussie.</summary>
    public int SuccessPoints { get; set; } = 10;

    public int PartialPoints { get; set; } = 5;

    public int FailurePoints { get; set; }

    /// <summary>Mission dont le résultat décide si celle-ci est jouée (voir <see cref="Condition"/>).</summary>
    public Guid? ConditionMissionId { get; set; }

    public MissionCondition Condition { get; set; }
}
