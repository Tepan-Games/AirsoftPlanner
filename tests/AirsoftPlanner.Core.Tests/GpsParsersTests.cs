using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Core.Gps;
using AirsoftPlanner.Core.Registration;

namespace AirsoftPlanner.Core.Tests;

public class GpsParsersTests
{
    [Fact]
    public void OsmAnd_query_from_Traccar_Client()
    {
        var fix = GpsParsers.FromOsmAndQuery(new Dictionary<string, string>
        {
            ["id"] = "alpha", ["lat"] = "48.405", ["lon"] = "2.699", ["timestamp"] = "1791015600", ["accuracy"] = "8",
        });

        Assert.NotNull(fix);
        Assert.Equal("alpha", fix.DeviceId);
        Assert.Equal(new GeoPoint(48.405, 2.699), fix.Point);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1791015600), fix.Time);
    }

    [Fact]
    public void OsmAnd_query_accepts_millisecond_timestamps_and_location_parameter()
    {
        var fix = GpsParsers.FromOsmAndQuery(new Dictionary<string, string>
        {
            ["deviceid"] = "B1", ["location"] = "48.4,2.7", ["timestamp"] = "1791015600000",
        });

        Assert.Equal(new GeoPoint(48.4, 2.7), fix!.Point);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1791015600), fix.Time);
    }

    [Fact]
    public void Incomplete_or_invalid_queries_are_ignored()
    {
        Assert.Null(GpsParsers.FromOsmAndQuery(new Dictionary<string, string> { ["lat"] = "48", ["lon"] = "2" }));
        Assert.Null(GpsParsers.FromOsmAndQuery(new Dictionary<string, string> { ["id"] = "a", ["lat"] = "120", ["lon"] = "2" }));
    }

    [Fact]
    public void Traccar_Client_json()
    {
        const string json = """{"location":{"timestamp":"2026-10-03T10:20:00.000Z","coords":{"latitude":48.405,"longitude":2.699,"accuracy":5}},"device_id":"charlie"}""";

        var fix = GpsParsers.FromTraccarClientJson(json);

        Assert.Equal(("charlie", new GeoPoint(48.405, 2.699)), (fix!.DeviceId, fix.Point));
        Assert.Equal(new DateTimeOffset(2026, 10, 3, 10, 20, 0, TimeSpan.Zero), fix.Time);
    }

    [Fact]
    public void Meshtastic_position_message()
    {
        const string json = """{"channel":0,"from":2712847316,"id":12345,"payload":{"altitude":95,"latitude_i":484050000,"longitude_i":26990000,"time":1791015600},"sender":"!b40d1a4c","timestamp":1791015601,"to":4294967295,"type":"position"}""";

        var fix = GpsParsers.FromMeshtasticJson(json);

        Assert.Equal("!a1b2c3d4", fix!.DeviceId);
        Assert.Equal(new GeoPoint(48.405, 2.699), fix.Point);
        Assert.Equal("Meshtastic", fix.Source);
    }

    [Fact]
    public void Meshtastic_messages_other_than_positions_are_ignored()
    {
        Assert.Null(GpsParsers.FromMeshtasticJson("""{"from":1,"type":"text","payload":{"text":"bonjour"}}"""));
        Assert.Null(GpsParsers.FromMeshtasticJson("""{"from":1,"type":"position","payload":{"latitude_i":0,"longitude_i":0}}"""));
    }

    [Fact]
    public void Generic_json_array()
    {
        var fixes = GpsParsers.FromGenericJson("""[{"deviceId":"a","latitude":48.4,"longitude":2.7,"time":"2026-10-03T10:00:00+02:00"},{"deviceId":"b","lat":"48.5","lon":"2.8"},{"deviceId":"c"}]""");

        Assert.Equal(["a", "b"], fixes.Select(f => f.DeviceId));
        Assert.Null(fixes[1].Time);
    }

    [Fact]
    public void Gpx_track()
    {
        const string gpx = """
            <gpx version="1.1" xmlns="http://www.topografix.com/GPX/1/1">
              <trk><trkseg>
                <trkpt lat="48.401" lon="2.695"><time>2026-10-03T08:00:00Z</time></trkpt>
                <trkpt lat="48.402" lon="2.696"><time>2026-10-03T08:05:00Z</time></trkpt>
              </trkseg></trk>
            </gpx>
            """;

        var fixes = GpsParsers.FromGpx(gpx, "alpha");

        Assert.Equal(2, fixes.Count);
        Assert.Equal(new GeoPoint(48.402, 2.696), fixes[1].Point);
        Assert.All(fixes, f => Assert.Equal("alpha", f.DeviceId));
    }

    [Fact]
    public void Csv_positions_with_french_decimals()
    {
        var fixes = GpsParsers.FromCsv("Équipe;Heure;Latitude;Longitude\nAlpha;03/10/2026 10:20;48,405;2,699\n");

        var fix = Assert.Single(fixes);
        Assert.Equal(("Alpha", new GeoPoint(48.405, 2.699)), (fix.DeviceId, fix.Point));
        Assert.Equal(new DateTime(2026, 10, 3, 10, 20, 0), fix.Time!.Value.LocalDateTime);
    }

    [Fact]
    public void Devices_are_matched_by_identifier_then_by_team_name()
    {
        var alpha = new Team { Name = "Alpha", GpsDeviceIds = "alpha-tel, !a1b2c3d4" };
        var bravo = new Team { Name = "Bravo" };
        var cancelled = new Team { Name = "Delta", GpsDeviceIds = "delta", Status = RegistrationStatus.Cancelled };
        Team[] teams = [alpha, bravo, cancelled];

        Assert.Same(alpha, GpsParsers.FindTeam(teams, "!A1B2C3D4"));
        Assert.Same(alpha, GpsParsers.FindTeam(teams, "alpha-tel"));
        Assert.Same(bravo, GpsParsers.FindTeam(teams, "bravo"));
        Assert.Null(GpsParsers.FindTeam(teams, "delta"));
        Assert.Null(GpsParsers.FindTeam(teams, "inconnu"));
    }
}
