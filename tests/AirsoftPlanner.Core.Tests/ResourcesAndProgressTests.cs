using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Core.Geo;
using AirsoftPlanner.Core.Planning;

namespace AirsoftPlanner.Core.Tests;

public class ResourcesAndProgressTests
{
    private static readonly Guid Alpha = Guid.NewGuid();
    private static readonly Guid Bravo = Guid.NewGuid();

    private static Mission At(int startMinutes, int duration, params Guid[] teams) =>
        new() { StartMinutes = startMinutes, DurationMinutes = duration, TeamIds = [.. teams] };

    // ----- Effectif maximum -----

    [Fact]
    public void Too_many_players_on_a_mission_is_reported()
    {
        var mission = At(600, 60, Alpha, Bravo);
        mission.MaxPlayers = 12;
        var sizes = new Dictionary<Guid, int> { [Alpha] = 8, [Bravo] = 6 };

        var issues = ScheduleAnalyzer.Analyze([mission], 540, 1080, new ScheduleResources(sizes, new Dictionary<Guid, GameItem>()));

        Assert.Equal(ScheduleIssueKind.TooManyPlayers, Assert.Single(issues).Kind);
        mission.MaxPlayers = 14;
        Assert.Empty(ScheduleAnalyzer.Analyze([mission], 540, 1080, new ScheduleResources(sizes, new Dictionary<Guid, GameItem>())));
    }

    // ----- Matériel de jeu -----

    private static ScheduleResources Stock(GameItem item) =>
        new(new Dictionary<Guid, int>(), new Dictionary<Guid, GameItem> { [item.Id] = item });

    [Fact]
    public void Consumable_items_are_limited_over_the_whole_operation()
    {
        var smoke = new GameItem { Name = "Fumigène", Quantity = 5, IsConsumable = true };
        var first = At(600, 30, Alpha);
        var second = At(900, 30, Bravo);
        first.Items.Add(new MissionItemUse(smoke.Id, 3));
        second.Items.Add(new MissionItemUse(smoke.Id, 3));

        var issues = ScheduleAnalyzer.Analyze([first, second], 540, 1080, Stock(smoke));

        Assert.Equal(2, issues.Count(i => i.Kind == ScheduleIssueKind.ItemShortage && i.ItemId == smoke.Id));
        Assert.Equal(6, ScheduleAnalyzer.TotalItemUse([first, second])[smoke.Id]);
    }

    [Fact]
    public void Reusable_items_are_limited_only_when_used_at_the_same_time()
    {
        var crate = new GameItem { Name = "Caisse", Quantity = 2 };
        var morning = At(600, 60, Alpha);
        var afternoon = At(900, 60, Bravo);
        var overlapping = At(630, 60, Bravo);
        foreach (var mission in new[] { morning, afternoon, overlapping })
            mission.Items.Add(new MissionItemUse(crate.Id, 2));

        Assert.Empty(ScheduleAnalyzer.Analyze([morning, afternoon], 540, 1080, Stock(crate)));

        var issues = ScheduleAnalyzer.Analyze([morning, afternoon, overlapping], 540, 1080, Stock(crate));
        Assert.Equal(new[] { morning.Id, overlapping.Id }.Order(),
            issues.Where(i => i.Kind == ScheduleIssueKind.ItemShortage).Select(i => i.MissionId).Order());
    }

    [Fact]
    public void Disabled_missions_do_not_use_items()
    {
        var smoke = new GameItem { Quantity = 1, IsConsumable = true };
        var used = At(600, 30, Alpha);
        var cancelled = At(700, 30, Alpha);
        cancelled.IsEnabled = false;
        used.Items.Add(new MissionItemUse(smoke.Id, 1));
        cancelled.Items.Add(new MissionItemUse(smoke.Id, 1));

        Assert.Empty(ScheduleAnalyzer.Analyze([used, cancelled], 540, 1080, Stock(smoke)));
    }

    // ----- Distances -----

    private static readonly Zone Square = new()
    {
        Kind = ZoneKind.Area,
        Points = [new GeoPoint(45.001, 5.000), new GeoPoint(45.001, 5.001), new GeoPoint(45.000, 5.001), new GeoPoint(45.000, 5.000)],
    };

    [Fact]
    public void Distance_is_zero_inside_a_zone_and_measured_to_the_nearest_edge_outside()
    {
        Assert.Equal(0, GeoMath.DistanceToZone(new GeoPoint(45.0005, 5.0005), Square));

        // 0,001° de latitude ≈ 111 m au sud du bord sud.
        Assert.InRange(GeoMath.DistanceToZone(new GeoPoint(44.999, 5.0005), Square), 108, 114);
    }

    [Fact]
    public void Haversine_distance_is_plausible()
    {
        // Tour Eiffel → Arc de Triomphe ≈ 1,7 km
        Assert.InRange(GeoMath.DistanceMeters(new GeoPoint(48.85837, 2.29448), new GeoPoint(48.87378, 2.29504)), 1690, 1730);
    }

    // ----- Suivi de l'avancement -----

    private static readonly Dictionary<Guid, Zone> Zones = new() { [Square.Id] = Square };

    private static TeamProgress Evaluate(Mission mission, GeoPoint position, double now) =>
        ProgressTracker.Evaluate(Alpha, [mission], Zones, (position, now - 2), now, walkingSpeedKmh: 3);

    [Fact]
    public void Team_far_from_its_next_zone_is_late()
    {
        var mission = At(600, 30, Alpha);
        mission.ZoneId = Square.Id;
        // ≈ 1,1 km au sud : 22 min de marche à 3 km/h.
        var far = new GeoPoint(44.990, 5.0005);

        Assert.Equal(ProgressStatus.OnTime, Evaluate(mission, far, now: 560).Status); // 40 min avant
        Assert.Equal(ProgressStatus.Tight, Evaluate(mission, far, now: 572).Status); // 28 min avant
        var late = Evaluate(mission, far, now: 590); // 10 min avant
        Assert.Equal(ProgressStatus.Late, late.Status);
        Assert.InRange(late.TravelMinutes!.Value, 20, 24);
        Assert.True(late.SlackMinutes < 0);
    }

    [Fact]
    public void Team_not_in_zone_during_its_mission_is_late_and_team_in_zone_is_on_time()
    {
        var mission = At(600, 30, Alpha);
        mission.ZoneId = Square.Id;

        Assert.Equal(ProgressStatus.Late, Evaluate(mission, new GeoPoint(44.995, 5.0005), now: 610).Status);
        Assert.Equal(ProgressStatus.OnTime, Evaluate(mission, new GeoPoint(45.0005, 5.0005), now: 610).Status);
    }

    [Fact]
    public void Unknown_and_idle_teams()
    {
        var mission = At(600, 30, Alpha);

        Assert.Equal(ProgressStatus.Unknown,
            ProgressTracker.Evaluate(Alpha, [mission], Zones, null, 500, 3).Status);
        Assert.Equal(ProgressStatus.Idle, Evaluate(mission, new GeoPoint(45, 5), now: 700).Status);
    }
}
