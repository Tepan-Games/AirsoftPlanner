using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Core.Registration;

namespace AirsoftPlanner.Core.Gps;

/// <summary>Position reçue d'un appareil (smartphone, boîtier Meshtastic, serveur de suivi...).</summary>
/// <param name="DeviceId">Identifiant de l'appareil, associé à une équipe dans l'onglet Équipes.</param>
/// <param name="Point">Position.</param>
/// <param name="Time">Heure de la mesure, si l'appareil la fournit.</param>
/// <param name="Source">Origine (« Smartphone », « Meshtastic », « Traccar », « Fichier »...).</param>
public record GpsFix(string DeviceId, GeoPoint Point, DateTimeOffset? Time, string Source);

/// <summary>Lecture des formats de positions GPS les plus courants.</summary>
public static class GpsParsers
{
    /// <summary>
    /// Protocole OsmAnd en paramètres d'URL, utilisé par Traccar Client, OsmAnd, GPSLogger... :
    /// <c>?id=alpha&amp;lat=48.85&amp;lon=2.35&amp;timestamp=1700000000</c>.
    /// </summary>
    public static GpsFix? FromOsmAndQuery(IReadOnlyDictionary<string, string> query, string source = "Smartphone")
    {
        string? Get(params string[] keys) => keys.Select(k => query.FirstOrDefault(q => q.Key.Equals(k, StringComparison.OrdinalIgnoreCase)).Value)
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

        var id = Get("id", "deviceid", "device_id");
        double? lat = Number(Get("lat", "latitude")), lon = Number(Get("lon", "lng", "longitude"));
        if ((lat is null || lon is null) && Get("location") is { } location && location.Split(',') is [var a, var b])
            (lat, lon) = (Number(a), Number(b));
        if (id is null || lat is null || lon is null)
            return null;

        var point = new GeoPoint(lat.Value, lon.Value);
        return point.IsValid ? new GpsFix(id.Trim(), point, Time(Get("timestamp", "time")), source) : null;
    }

    /// <summary>Format JSON de Traccar Client (versions récentes) : <c>{"device_id":"alpha","location":{"timestamp":...,"coords":{...}}}</c>.</summary>
    public static GpsFix? FromTraccarClientJson(string json, string source = "Smartphone")
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("device_id", out var id) || !root.TryGetProperty("location", out var location)
            || !location.TryGetProperty("coords", out var coords))
            return null;

        var point = new GeoPoint(coords.GetProperty("latitude").GetDouble(), coords.GetProperty("longitude").GetDouble());
        var time = location.TryGetProperty("timestamp", out var t) ? Time(t.ToString()) : null;
        return point.IsValid ? new GpsFix(id.ToString(), point, time, source) : null;
    }

    /// <summary>
    /// Message JSON publié par un nœud Meshtastic (passerelle MQTT, option JSON activée) ; seuls les messages
    /// de type « position » sont retenus. L'identifiant est celui du nœud (ex. « !a1b2c3d4 »).
    /// </summary>
    public static GpsFix? FromMeshtasticJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("type", out var type) || type.GetString() != "position" || !root.TryGetProperty("payload", out var payload)
            || !payload.TryGetProperty("latitude_i", out var latI) || !payload.TryGetProperty("longitude_i", out var lonI))
            return null;

        // Le nœud émetteur (« from », numérique) s'écrit « !a1b2c3d4 » dans l'application Meshtastic.
        var id = root.TryGetProperty("from", out var from) && from.ValueKind == JsonValueKind.Number ? $"!{from.GetUInt32():x8}"
            : root.TryGetProperty("sender", out var sender) ? sender.GetString()
            : null;
        if (string.IsNullOrEmpty(id))
            return null;

        var point = new GeoPoint(latI.GetInt64() / 1e7, lonI.GetInt64() / 1e7);
        var time = payload.TryGetProperty("time", out var t) && t.GetInt64() > 0 ? DateTimeOffset.FromUnixTimeSeconds(t.GetInt64()) : (DateTimeOffset?)null;
        return point.IsValid && point != new GeoPoint(0, 0) ? new GpsFix(id, point, time, "Meshtastic") : null;
    }

    /// <summary>Format JSON simple pour d'autres systèmes : <c>[{"deviceId":"alpha","latitude":48.85,"longitude":2.35,"time":"2026-10-03T10:20:00+02:00"}]</c>.</summary>
    public static IReadOnlyList<GpsFix> FromGenericJson(string json, string source = "Webservice")
    {
        using var document = JsonDocument.Parse(json);
        var items = document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement.EnumerateArray().ToList() : [document.RootElement];
        var fixes = new List<GpsFix>();
        foreach (var item in items)
        {
            string? Text(params string[] names) => names.Select(n => item.TryGetProperty(n, out var v) ? v.ToString() : null).FirstOrDefault(v => v is not null);
            var id = Text("deviceId", "device_id", "id");
            double? lat = Number(Text("latitude", "lat")), lon = Number(Text("longitude", "lon", "lng"));
            if (id is null || lat is null || lon is null || !new GeoPoint(lat.Value, lon.Value).IsValid)
                continue;
            fixes.Add(new GpsFix(id, new GeoPoint(lat.Value, lon.Value), Time(Text("time", "timestamp", "fixTime")), source));
        }

        return fixes;
    }

    /// <summary>Trace GPX (points de trace et points de passage horodatés), attribuée à un appareil ou une équipe.</summary>
    public static IReadOnlyList<GpsFix> FromGpx(string xml, string deviceId)
    {
        var document = XDocument.Parse(xml);
        return document.Descendants()
            .Where(e => e.Name.LocalName is "trkpt" or "wpt" or "rtept")
            .Select(e => (Lat: Number(e.Attribute("lat")?.Value), Lon: Number(e.Attribute("lon")?.Value),
                Time: Time(e.Elements().FirstOrDefault(c => c.Name.LocalName == "time")?.Value)))
            .Where(p => p.Lat is not null && p.Lon is not null)
            .Select(p => new GpsFix(deviceId, new GeoPoint(p.Lat!.Value, p.Lon!.Value), p.Time, "Fichier GPX"))
            .Where(f => f.Point.IsValid)
            .OrderBy(f => f.Time)
            .ToList();
    }

    /// <summary>
    /// Positions en CSV : colonnes reconnues par leur intitulé (« Équipe » ou « Appareil », « Latitude », « Longitude », « Heure »).
    /// </summary>
    public static IReadOnlyList<GpsFix> FromCsv(string text)
    {
        var lines = text.TrimStart('﻿').Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2)
            return [];

        var separator = new[] { ';', '\t', ',' }.OrderByDescending(c => lines[0].Count(x => x == c)).First();
        var header = lines[0].Split(separator).Select(h => h.Trim().ToLowerInvariant()).ToList();
        int Column(params string[] keys) => header.FindIndex(h => keys.Any(h.Contains));
        var (idCol, latCol, lonCol, timeCol) = (Column("équipe", "equipe", "team", "appareil", "device", "id"),
            Column("lat"), Column("lon", "lng"), Column("heure", "time", "date"));
        if (idCol < 0 || latCol < 0 || lonCol < 0)
            throw new FormatException("Colonnes « Équipe » (ou « Appareil »), « Latitude » et « Longitude » attendues.");

        var fixes = new List<GpsFix>();
        foreach (var line in lines.Skip(1))
        {
            var fields = line.Split(separator);
            string Field(int i) => i >= 0 && i < fields.Length ? fields[i].Trim() : "";
            // Avec la virgule comme séparateur, les nombres utilisent le point ; sinon les deux sont acceptés.
            double? lat = Number(Field(latCol)), lon = Number(Field(lonCol));
            if (Field(idCol).Length == 0 || lat is null || lon is null)
                continue;
            fixes.Add(new GpsFix(Field(idCol), new GeoPoint(lat.Value, lon.Value), Time(Field(timeCol)), "Fichier CSV"));
        }

        return fixes;
    }

    /// <summary>Équipe associée à un identifiant d'appareil (liste « Identifiants GPS » de l'équipe), ou au nom de l'équipe.</summary>
    public static Team? FindTeam(IEnumerable<Team> teams, string deviceId)
    {
        var id = deviceId.Trim();
        var candidates = teams.Where(t => RegistrationRules.IsPlaying(t.Status) || t.Status == RegistrationStatus.WaitingList).ToList();
        return candidates.FirstOrDefault(t => DeviceIds(t).Contains(id, StringComparer.OrdinalIgnoreCase))
               ?? candidates.FirstOrDefault(t => t.Name.Trim().Equals(id, StringComparison.CurrentCultureIgnoreCase));
    }

    public static IReadOnlyList<string> DeviceIds(Team team) =>
        team.GpsDeviceIds.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static double? Number(string? text) =>
        double.TryParse(text?.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

    /// <summary>Heure Unix (secondes ou millisecondes) ou date ISO 8601.</summary>
    private static DateTimeOffset? Time(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix))
            return unix > 100_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds(unix) : DateTimeOffset.FromUnixTimeSeconds(unix);
        // Dates avec « / » : jour/mois à la française ; sinon ISO 8601 (2026-10-03T10:20:00Z).
        var culture = text.Contains('/') ? CultureInfo.GetCultureInfo("fr-FR") : CultureInfo.InvariantCulture;
        return DateTimeOffset.TryParse(text, culture, DateTimeStyles.AssumeLocal, out var date) ? date : null;
    }
}
