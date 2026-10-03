using System.Text.Json;
using AirsoftPlanner.Core.Geo;
using AirsoftPlanner.Core.Gps;

namespace AirsoftPlanner.Core.Tests;

/// <summary>Le téléphone doit comprendre exactement ce que le serveur du PC envoie (et inversement).</summary>
public class ProtocolJsonTests
{
    [Fact]
    public void Track_response_sent_by_the_server_is_understood_by_the_phone()
    {
        // Réponse réelle du serveur (capturée lors d'un essai), raccourcie.
        const string json = """
            {"team":"Alpha","intervalSeconds":30,"shareMode":"Coordinates",
             "allies":[{"team":"Charlie","latitude":48.4044,"longitude":2.6973,"coordinates":"31U 477958 5361290","time":"2026-10-03T19:55:48+02:00","radioFrequency":""}],
             "mission":{"name":"Reconnaissance du village","isCurrent":true,"start":"2026-10-03T10:00:00+02:00","end":"2026-10-03T11:00:00+02:00",
                        "zone":"Village","zoneCoordinates":"31U 477697 5361398","zoneLatitude":48.405,"zoneLongitude":2.698,"briefing":"Repérer","equipment":"1 × Dossier"},
             "map":null,
             "comms":{"faction":"OTAN","factionFrequency":"PMR 446 canal 1","teams":[{"team":"Alpha","frequency":"PMR 2","isCommand":true}],
                      "orgaFrequency":"PMR 446 canal 8","emergencyPhone":"06 99 99 99 99"},
             "coordinateFormat":"Utm"}
            """;

        var response = JsonSerializer.Deserialize(json, ProtocolJson.Default.TrackResponse)!;

        Assert.Equal(("Alpha", 30, AllyShareMode.Coordinates, CoordinateFormat.Utm),
            (response.Team, response.IntervalSeconds, response.ShareMode, response.CoordinateFormat));
        Assert.Equal("Charlie", Assert.Single(response.Allies!).Team);
        Assert.True(response.Mission!.IsCurrent);
        Assert.Equal("06 99 99 99 99", response.Comms!.EmergencyPhone);
        Assert.True(response.Comms.Teams[0].IsCommand);
    }

    [Fact]
    public void Phone_requests_use_the_names_expected_by_the_server()
    {
        var json = JsonSerializer.Serialize(new TrackRequest("ABC", [new TrackPoint(48.4, 2.7, 5, DateTimeOffset.UnixEpoch)]),
            ProtocolJson.Default.TrackRequest);

        Assert.Contains("\"token\":\"ABC\"", json);
        Assert.Contains("\"positions\":[{\"latitude\":48.4", json);
    }
}
