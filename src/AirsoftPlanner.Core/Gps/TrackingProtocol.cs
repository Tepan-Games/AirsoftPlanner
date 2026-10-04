namespace AirsoftPlanner.Core.Gps;

// Messages JSON échangés entre l'application Android et le serveur du PC de l'OP.
// Partagés par les deux programmes : une modification ici s'applique des deux côtés.

/// <summary>Enrôlement : <c>POST /api/enroll</c> avec le code de l'équipe.</summary>
public record EnrollRequest(string Code, string DeviceName);

/// <summary>Réponse à l'enrôlement : jeton à conserver et rappel de l'équipe, de l'OP et de la radio.</summary>
/// <param name="OperationId">Identifiant de l'OP : permet au téléphone de retrouver le PC de son OP sur le Wi-Fi (<see cref="Discovery"/>).</param>
public record EnrollResponse(string Token, string Team, string Operation, string Faction, string RadioFrequency, int IntervalSeconds,
    AllyShareMode ShareMode = AllyShareMode.Coordinates, Comms? Comms = null, Guid OperationId = default);

/// <summary>Fréquence d'une équipe de la faction.</summary>
public record TeamFrequency(string Team, string Frequency, bool IsCommand);

/// <summary>
/// Plan de communication de l'équipe : fréquences de sa faction et des équipes alliées, fréquence de l'orga
/// et numéro d'urgence (toujours envoyés, quel que soit le partage des positions).
/// </summary>
public record Comms(string Faction, string FactionFrequency, IReadOnlyList<TeamFrequency> Teams, string OrgaFrequency, string EmergencyPhone);

/// <summary>Ce que les téléphones voient des équipes alliées (réglage de l'OP).</summary>
/// <remarks>Coordinates vient en premier : c'est le réglage des OP créées avant cette option. Échangé en texte (JSON).</remarks>
public enum AllyShareMode
{
    /// <summary>Positions des alliés en coordonnées seulement (version difficile).</summary>
    Coordinates,

    /// <summary>Rien : seule la position de sa propre équipe est envoyée.</summary>
    None,

    /// <summary>Positions des alliés et de sa propre équipe sur la carte du terrain.</summary>
    Map,
}

/// <summary>Position d'une équipe alliée (même faction), avec ses coordonnées déjà formatées selon l'OP.</summary>
public record AllyPosition(string Team, double Latitude, double Longitude, string Coordinates, DateTimeOffset Time, string RadioFrequency);

/// <summary>Récapitulatif d'une mission pour le chef d'équipe.</summary>
/// <param name="IsCurrent">Vrai si elle est en cours, faux si c'est la prochaine.</param>
/// <param name="Id">Identifiant de la mission : le téléphone signale une nouvelle mission.</param>
public record MissionBrief(string Name, bool IsCurrent, DateTimeOffset Start, DateTimeOffset End, string Zone, string ZoneCoordinates,
    double? ZoneLatitude, double? ZoneLongitude, string Briefing, string Equipment, Guid Id = default);

/// <summary>Coordonnées d'un sommet.</summary>
public record LatLon(double Latitude, double Longitude);

/// <summary>Point d'intérêt communiqué à l'équipe (bivouac, campement, respawn...), selon la visibilité choisie par l'orga.</summary>
/// <param name="Symbol">Symbole court (⛺, ✚...).</param>
/// <param name="Coordinates">Coordonnées du centre, déjà formatées selon l'OP.</param>
/// <param name="Outline">Contour d'une zone (vide pour un point).</param>
public record PoiInfo(string Name, string Category, string Symbol, string Coordinates, double Latitude, double Longitude,
    string Description, string Color, IReadOnlyList<LatLon> Outline);

/// <summary>Message de l'orga reçu par le téléphone (texte libre ou annonce de mission).</summary>
/// <param name="Audience">« Toutes les équipes », « Faction OTAN » ou « Équipe ».</param>
/// <param name="Sender">Orga (organisation) ou QG (ordres en jeu).</param>
/// <param name="Mission">Mission diffusée à l'équipe au moment du message (vide : hors mission).</param>
/// <param name="HasPhoto">Photo jointe, à télécharger par <c>GET /api/message/photo</c>.</param>
public record PhoneMessage(Guid Id, DateTimeOffset SentAt, string Text, string Audience, AirsoftPlanner.Core.Domain.MessageKind Kind,
    AirsoftPlanner.Core.Domain.MessageSender Sender = AirsoftPlanner.Core.Domain.MessageSender.Orga, string Mission = "", bool HasPhoto = false);

/// <summary>Fond de carte partagé avec les téléphones (mode carte) : l'image s'obtient par <c>GET /api/map/image</c>.</summary>
public record MapInfo(string Name, string Attribution, double North, double South, double West, double East);

/// <summary>Une position mesurée par le téléphone.</summary>
public record TrackPoint(double Latitude, double Longitude, double? Accuracy, DateTimeOffset Time);

/// <summary>
/// Envoi de positions : <c>POST /api/track</c>. Plusieurs positions possibles (celles accumulées pendant
/// une coupure du Wi-Fi sont envoyées d'un coup au retour du réseau).
/// </summary>
public record TrackRequest(string Token, IReadOnlyList<TrackPoint> Positions);

/// <summary>
/// Réponse à un envoi : intervalle à respecter (réglable depuis le PC pendant l'OP), mission en cours ou à venir,
/// et positions des alliés selon le réglage de l'OP.
/// </summary>
public record TrackResponse(string Team, int IntervalSeconds, AllyShareMode ShareMode = AllyShareMode.Coordinates,
    IReadOnlyList<AllyPosition>? Allies = null, MissionBrief? Mission = null, MapInfo? Map = null, Comms? Comms = null,
    AirsoftPlanner.Core.Geo.CoordinateFormat CoordinateFormat = AirsoftPlanner.Core.Geo.CoordinateFormat.Utm,
    IReadOnlyList<PhoneMessage>? Messages = null, IReadOnlyList<PoiInfo>? Points = null);

/// <summary>Contenu du QR code d'enrôlement : <c>airsoftplanner://enroll?server=...&amp;code=...</c>.</summary>
public static class EnrollmentLink
{
    public const string Scheme = "airsoftplanner";

    /// <summary>
    /// Adresse complète du serveur à partir de ce que l'orga a saisi (« monop.duckdns.org », « 192.168.1.20:5055 »,
    /// « http://... ») : schéma http et port du serveur ajoutés s'ils manquent.
    /// </summary>
    public static string ServerUrl(string address, int defaultPort)
    {
        var text = address.Trim().TrimEnd('/');
        if (text.Length == 0)
            return "";
        if (!text.Contains("://", StringComparison.Ordinal))
            text = "http://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
            return text;
        var hasPort = uri.Authority.Contains(':', StringComparison.Ordinal) && !uri.Authority.EndsWith(']');
        return $"{uri.Scheme}://{uri.Host}:{(hasPort ? uri.Port : defaultPort)}";
    }

    public static string Create(string serverUrl, string code) =>
        $"{Scheme}://enroll?server={Uri.EscapeDataString(serverUrl)}&code={Uri.EscapeDataString(code)}";

    public static bool TryParse(string link, out string serverUrl, out string code)
    {
        serverUrl = code = "";
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) || uri.Scheme != Scheme)
            return false;

        var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
        serverUrl = query.GetValueOrDefault("server", "");
        code = query.GetValueOrDefault("code", "");
        return serverUrl.Length > 0 && code.Length > 0;
    }
}
