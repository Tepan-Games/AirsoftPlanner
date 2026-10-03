using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Core.Planning;

namespace AirsoftPlanner.Core.Tests;

public class ItemTrackerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 3, 9, 0, 0, TimeSpan.FromHours(2));
    private static readonly Guid Alpha = Guid.NewGuid();
    private static readonly Guid Bravo = Guid.NewGuid();
    private static readonly GeoPoint Cache = new(45.0, 5.0);
    private static readonly GeoPoint AlphaSeen = new(45.01, 5.01);

    private static GeoPoint? Positions(Guid team, DateTimeOffset at) => team == Alpha && at >= T0.AddMinutes(30) ? AlphaSeen : null;

    private static readonly ItemEvent[] History =
    [
        new() { Kind = ItemEventKind.Placed, Location = Cache, At = T0 },
        new() { Kind = ItemEventKind.PickedUp, TeamId = Alpha, Location = Cache, At = T0.AddMinutes(20) },
        new() { Kind = ItemEventKind.Transferred, TeamId = Bravo, At = T0.AddMinutes(60) },
        new() { Kind = ItemEventKind.Returned, At = T0.AddMinutes(120) },
    ];

    [Fact]
    public void Item_placed_on_the_field_is_at_its_cache()
    {
        var state = ItemTracker.StateAt(History, T0.AddMinutes(10), Positions);

        Assert.Equal((ItemEventKind.Placed, (Guid?)null, (GeoPoint?)Cache, true), (state.Kind, state.HolderTeamId, state.Location, state.OnField));
    }

    [Fact]
    public void Item_held_by_a_team_follows_the_team()
    {
        var justPicked = ItemTracker.StateAt(History, T0.AddMinutes(25), Positions);
        Assert.Equal(Alpha, justPicked.HolderTeamId);
        Assert.Equal(Cache, justPicked.Location); // l'équipe n'a pas encore été revue : lieu de la récupération

        var later = ItemTracker.StateAt(History, T0.AddMinutes(45), Positions);
        Assert.Equal(AlphaSeen, later.Location);
        Assert.Equal(T0.AddMinutes(20), later.Since);
    }

    [Fact]
    public void Transfer_changes_the_holder_and_return_ends_field_tracking()
    {
        Assert.Equal(Bravo, ItemTracker.StateAt(History, T0.AddMinutes(90), Positions).HolderTeamId);

        var returned = ItemTracker.StateAt(History, T0.AddMinutes(130), Positions);
        Assert.False(returned.OnField);
        Assert.Null(returned.HolderTeamId);
    }

    [Fact]
    public void No_history_means_unknown()
    {
        Assert.Null(ItemTracker.StateAt([], T0, Positions).Kind);
        Assert.Null(ItemTracker.StateAt(History, T0.AddMinutes(-5), Positions).Kind);
    }
}
