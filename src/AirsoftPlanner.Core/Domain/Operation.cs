using AirsoftPlanner.Core.Geo;

namespace AirsoftPlanner.Core.Domain;

/// <summary>Une OP (partie organisée). Un fichier d'OP contient exactement une opération.</summary>
public class Operation : Entity
{
    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    public string Location { get; set; } = "";

    /// <summary>Équipe ou association qui organise l'OP.</summary>
    public string OrganizerName { get; set; } = "";

    public DateTimeOffset StartsAt { get; set; }

    public DateTimeOffset EndsAt { get; set; }

    /// <summary>Format des coordonnées affichées dans l'OP et ses documents.</summary>
    public CoordinateFormat CoordinateFormat { get; set; } = CoordinateFormat.Utm;

    /// <summary>Vitesse de déplacement à pied retenue pour estimer les retards (terrain, équipement, prudence).</summary>
    public double WalkingSpeedKmh { get; set; } = 3;

    /// <summary>Vitesse estimée des équipes qui ont un véhicule en jeu (pistes, chemins forestiers).</summary>
    public double VehicleSpeedKmh { get; set; } = 25;

    /// <summary>Participation aux frais demandée par joueur.</summary>
    public decimal PricePerPlayer { get; set; }

    /// <summary>Fréquence radio de l'orga (PC de l'OP, arbitres).</summary>
    public string OrgaRadioFrequency { get; set; } = "";

    /// <summary>Numéro de téléphone d'urgence de l'orga (blessure, incident).</summary>
    public string EmergencyPhone { get; set; } = "";

    /// <summary>Carburant remboursé par kilomètre aux véhicules mis en jeu.</summary>
    public decimal FuelRatePerKm { get; set; }

    /// <summary>Intervalle d'envoi des positions par l'application Android (secondes).</summary>
    public int TrackingIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Adresse publiée du PC de l'OP pour les téléphones (nom DynDNS, ex. « monop.duckdns.org:5055 ») :
    /// imprimée dans les packages et les QR codes. Vide : adresse IP actuelle du PC sur le réseau local.
    /// </summary>
    public string ServerAddress { get; set; } = "";

    /// <summary>Doublons de fréquences radio déclarés normaux par l'orga (signatures, voir RadioPlanCheck).</summary>
    public List<string> IgnoredRadioConflicts { get; set; } = [];

    /// <summary>Ancien réglage du partage des positions, remplacé par <see cref="HqDifficulty"/> (conservé pour les anciens fichiers).</summary>
    public AirsoftPlanner.Core.Gps.AllyShareMode AllyShareMode { get; set; } = AirsoftPlanner.Core.Gps.AllyShareMode.Coordinates;

    /// <summary>Niveau de difficulté de l'onglet QG des téléphones (chaque faction peut avoir le sien).</summary>
    public HqDifficulty HqDifficulty { get; set; } = HqDifficulty.Easy;

    /// <summary>Règlement de jeu : règles propres à l'OP ou règlement ACP (PDF officiel joint automatiquement aux documents).</summary>
    public AirsoftPlanner.Core.Documents.GameRuleSet RuleSet { get; set; }

    /// <summary>Phase de la partie déclarée par l'orga (début, pause, reprise, fin), annoncée aux téléphones.</summary>
    public GamePhase GamePhase { get; set; }

    public DateTimeOffset? GamePhaseSince { get; set; }
}
