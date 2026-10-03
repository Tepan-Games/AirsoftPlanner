using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Core.Planning;

namespace AirsoftPlanner.Core.Tests;

public class ScheduleAnalyzerTests
{
    private const int Start = 9 * 60;
    private const int End = 18 * 60;
    private static readonly Guid Alpha = Guid.NewGuid();
    private static readonly Guid Bravo = Guid.NewGuid();

    private static Mission At(int hour, int minutes, int duration, params Guid[] teams) => new()
    {
        Name = $"{hour}h{minutes:00}",
        StartMinutes = hour * 60 + minutes,
        DurationMinutes = duration,
        TeamIds = [.. teams],
    };

    private static IReadOnlyList<ScheduleIssue> Analyze(params Mission[] missions) =>
        ScheduleAnalyzer.Analyze(missions, Start, End);

    [Fact]
    public void A_clean_schedule_has_no_issue()
    {
        var first = At(10, 0, 60, Alpha);
        var second = At(11, 0, 30, Alpha);
        second.PredecessorIds.Add(first.Id);

        Assert.Empty(Analyze(first, second, At(10, 0, 90, Bravo)));
    }

    [Fact]
    public void Overlapping_missions_of_the_same_team_are_reported_on_both()
    {
        var first = At(10, 0, 60, Alpha);
        var second = At(10, 30, 30, Alpha, Bravo);

        var issues = Analyze(first, second);

        Assert.Equal(2, issues.Count);
        Assert.All(issues, i => Assert.Equal(ScheduleIssueKind.TeamOverlap, i.Kind));
        Assert.Contains(issues, i => i.MissionId == first.Id && i.OtherMissionId == second.Id && i.TeamId == Alpha);
    }

    [Fact]
    public void Back_to_back_missions_do_not_overlap()
    {
        Assert.Empty(Analyze(At(10, 0, 60, Alpha), At(11, 0, 60, Alpha)));
    }

    [Fact]
    public void Disabled_missions_are_ignored()
    {
        var disabled = At(10, 0, 60, Alpha);
        disabled.IsEnabled = false;

        Assert.Empty(Analyze(At(10, 0, 60, Alpha), disabled));
    }

    [Fact]
    public void Starting_before_a_predecessor_ends_is_reported()
    {
        var first = At(10, 0, 60, Alpha);
        var second = At(10, 45, 30, Bravo);
        second.PredecessorIds.Add(first.Id);

        var issue = Assert.Single(Analyze(first, second));
        Assert.Equal(new ScheduleIssue(second.Id, ScheduleIssueKind.PredecessorNotFinished, first.Id), issue);
    }

    [Fact]
    public void A_disabled_predecessor_is_reported()
    {
        var first = At(10, 0, 60, Alpha);
        first.IsEnabled = false;
        var second = At(11, 0, 30, Bravo);
        second.PredecessorIds.Add(first.Id);

        Assert.Equal(ScheduleIssueKind.PredecessorDisabled, Assert.Single(Analyze(first, second)).Kind);
    }

    [Fact]
    public void Missions_outside_operation_hours_or_without_team_are_reported()
    {
        var early = At(8, 30, 60, Alpha);
        var orphan = At(12, 0, 30);

        var issues = Analyze(early, orphan);

        Assert.Contains(new ScheduleIssue(early.Id, ScheduleIssueKind.OutsideOperation), issues);
        Assert.Contains(new ScheduleIssue(orphan.Id, ScheduleIssueKind.NoTeam), issues);
    }

    [Fact]
    public void Dependency_cycles_are_detected_and_prevented()
    {
        var a = At(10, 0, 30, Alpha);
        var b = At(11, 0, 30, Alpha);
        var c = At(12, 0, 30, Alpha);
        b.PredecessorIds.Add(a.Id);
        c.PredecessorIds.Add(b.Id);
        Mission[] missions = [a, b, c];

        Assert.True(ScheduleAnalyzer.WouldCreateCycle(missions, a.Id, c.Id));
        Assert.True(ScheduleAnalyzer.WouldCreateCycle(missions, a.Id, a.Id));
        Assert.False(ScheduleAnalyzer.WouldCreateCycle(missions, c.Id, a.Id));

        a.PredecessorIds.Add(c.Id);
        var cycle = Analyze(missions).Where(i => i.Kind == ScheduleIssueKind.DependencyCycle).Select(i => i.MissionId);
        Assert.Equal(new[] { a.Id, b.Id, c.Id }.Order(), cycle.Order());
    }
}
