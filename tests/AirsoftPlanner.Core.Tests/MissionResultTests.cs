using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Core.Planning;

namespace AirsoftPlanner.Core.Tests;

public class MissionResultTests
{
    private static readonly Guid Alpha = Guid.NewGuid();

    // Assaut 10:00-11:00 ; ensuite exploitation si réussite, repli si échec (11:00-12:00, même équipe).
    private static (Mission Assault, Mission Exploit, Mission Fallback) Branches()
    {
        var assault = new Mission { Name = "Assaut", StartMinutes = 600, DurationMinutes = 60, TeamIds = [Alpha] };
        var exploit = new Mission { Name = "Exploitation", StartMinutes = 660, DurationMinutes = 60, TeamIds = [Alpha],
            ConditionMissionId = assault.Id, Condition = MissionCondition.IfSuccess };
        var fallback = new Mission { Name = "Repli", StartMinutes = 660, DurationMinutes = 60, TeamIds = [Alpha],
            ConditionMissionId = assault.Id, Condition = MissionCondition.IfFailure };
        return (assault, exploit, fallback);
    }

    [Theory]
    [InlineData(MissionResult.NotEvaluated, null, null)]
    [InlineData(MissionResult.Success, true, false)]
    [InlineData(MissionResult.Partial, true, false)]
    [InlineData(MissionResult.Failure, false, true)]
    public void Conditions_follow_the_result_of_the_other_mission(MissionResult result, bool? exploitPlayed, bool? fallbackPlayed)
    {
        var (assault, exploit, fallback) = Branches();
        assault.Result = result;
        Mission[] all = [assault, exploit, fallback];
        Assert.Equal(exploitPlayed, MissionResults.IsConditionMet(exploit, all));
        Assert.Equal(fallbackPlayed, MissionResults.IsConditionMet(fallback, all));
        Assert.True(MissionResults.IsConditionMet(assault, all));
    }

    [Fact]
    public void Condition_on_a_removed_mission_is_ignored()
    {
        var (_, exploit, _) = Branches();
        Assert.True(MissionResults.IsConditionMet(exploit, [exploit]));
    }

    [Fact]
    public void Dispatch_suggests_the_branch_matching_the_result()
    {
        var (assault, exploit, fallback) = Branches();
        Mission[] all = [assault, exploit, fallback];

        // Résultat pas encore saisi : aucune des deux suites n'est proposée.
        var pending = DiffusionAdvisor.Advise(Alpha, all, assault.Id, [], 665)!;
        Assert.Null(pending.Next);

        assault.Result = MissionResult.Failure;
        Assert.Same(fallback, DiffusionAdvisor.Advise(Alpha, all, assault.Id, [], 665)!.Next);
        assault.Result = MissionResult.Success;
        Assert.Same(exploit, DiffusionAdvisor.Advise(Alpha, all, assault.Id, [], 665)!.Next);
    }

    [Fact]
    public void Alternative_branches_are_not_an_overlap_but_must_follow_the_deciding_mission()
    {
        var (assault, exploit, fallback) = Branches();
        Assert.True(MissionResults.AreAlternatives(exploit, fallback));
        Assert.False(MissionResults.AreAlternatives(assault, exploit));
        Assert.Empty(ScheduleAnalyzer.Analyze([assault, exploit, fallback], 540, 1080));

        exploit.StartMinutes = 630;
        var issue = Assert.Single(ScheduleAnalyzer.Analyze([assault, exploit, fallback], 540, 1080),
            i => i.Kind == ScheduleIssueKind.ConditionNotFinished);
        Assert.Equal((exploit.Id, assault.Id), (issue.MissionId, issue.OtherMissionId!.Value));
    }

    [Fact]
    public void Scores_count_each_mission_once_per_faction_and_for_every_team()
    {
        var blue = new Faction { Name = "OTAN", Color = "#1565C0" };
        var red = new Faction { Name = "Insurgés", Color = "#C62828" };
        var alpha = new Team { Name = "Alpha", FactionId = blue.Id };
        var bravo = new Team { Name = "Bravo", FactionId = blue.Id };
        var zulu = new Team { Name = "Zulu", FactionId = red.Id };
        Mission[] missions =
        [
            new() { Name = "Pont", TeamIds = [alpha.Id, bravo.Id], Result = MissionResult.Success, SuccessPoints = 10 },
            new() { Name = "Village", TeamIds = [zulu.Id], Result = MissionResult.Partial, PartialPoints = 4 },
            new() { Name = "Convoi", TeamIds = [zulu.Id], Result = MissionResult.Failure, FailurePoints = -2 },
            new() { Name = "Pas évaluée", TeamIds = [alpha.Id], SuccessPoints = 50 },
            new() { Name = "Désactivée", TeamIds = [alpha.Id], Result = MissionResult.Success, SuccessPoints = 50, IsEnabled = false },
        ];

        var factions = Scoreboard.ByFaction(missions, [blue, red], [alpha, bravo, zulu]);
        Assert.Equal([("OTAN", 10, 1), ("Insurgés", 2, 2)], factions.Select(f => (f.Name, f.Points, f.Evaluated)));

        var teams = Scoreboard.ByTeam(missions, [alpha, bravo, zulu]);
        Assert.Equal([("Alpha", 10), ("Bravo", 10), ("Zulu", 2)], teams.Select(t => (t.Name, t.Points)));
        Assert.Equal((0, 1, 1), (teams[2].Success, teams[2].Partial, teams[2].Failure));
    }
}
