using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Core.Planning;

namespace AirsoftPlanner.Core.Tests;

public class StrengthTrackerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 3, 9, 0, 0, TimeSpan.FromHours(2));
    private static readonly Guid Julien = Guid.NewGuid();
    private static readonly Guid Sophie = Guid.NewGuid();

    private static PlayerStatusEvent Out(Guid? member, int minutes, OutReason reason, int players = 1) =>
        new() { MemberId = member, IsOut = true, Reason = reason, Players = players, At = T0.AddMinutes(minutes) };

    private static PlayerStatusEvent Back(Guid? member, int minutes, int players = 1) =>
        new() { MemberId = member, IsOut = false, Players = players, At = T0.AddMinutes(minutes) };

    [Fact]
    public void Named_players_go_out_and_come_back()
    {
        PlayerStatusEvent[] events = [Out(Julien, 30, OutReason.RealInjury), Out(Sophie, 40, OutReason.Rest), Back(Sophie, 70)];

        var at50 = StrengthTracker.StrengthAt(8, events, T0.AddMinutes(50));
        Assert.Equal(6, at50.Present);
        Assert.Equal([Julien, Sophie], at50.Out.Select(o => o.MemberId!.Value));
        Assert.Equal(OutReason.RealInjury, at50.Out[0].Reason);

        var at80 = StrengthTracker.StrengthAt(8, events, T0.AddMinutes(80));
        Assert.Equal(7, at80.Present);
        Assert.Equal(Julien, Assert.Single(at80.Out).MemberId);
    }

    [Fact]
    public void Unnamed_players_are_counted_and_return_oldest_first()
    {
        PlayerStatusEvent[] events = [Out(null, 10, OutReason.Equipment, 2), Out(null, 20, OutReason.Sanction), Back(null, 30, 2)];

        var strength = StrengthTracker.StrengthAt(6, events, T0.AddMinutes(35));

        Assert.Equal(5, strength.Present);
        Assert.Equal(OutReason.Sanction, Assert.Single(strength.Out).Reason);
    }

    [Fact]
    public void Timeline_follows_each_change()
    {
        PlayerStatusEvent[] events = [Out(Julien, 30, OutReason.Rest), Back(Julien, 60), Out(null, 90, OutReason.Abandon, 3)];

        var timeline = StrengthTracker.Timeline(8, events);

        Assert.Equal([7, 8, 5], timeline.Select(t => t.Present));
    }

    [Fact]
    public void Present_never_goes_below_zero()
    {
        Assert.Equal(0, StrengthTracker.StrengthAt(2, [Out(null, 5, OutReason.Abandon, 5)], T0.AddMinutes(10)).Present);
    }
}
