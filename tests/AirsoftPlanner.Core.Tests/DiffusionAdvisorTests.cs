using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Core.Planning;

namespace AirsoftPlanner.Core.Tests;

public class DiffusionAdvisorTests
{
    private static readonly Guid Alpha = Guid.NewGuid();
    private static readonly Mission Recon = new() { Name = "Reconnaissance", TeamIds = [Alpha], StartMinutes = 600, DurationMinutes = 60 };
    private static readonly Mission Assault = new() { Name = "Assaut", TeamIds = [Alpha], StartMinutes = 690, DurationMinutes = 60 };
    private static readonly Mission Other = new() { Name = "Autre équipe", TeamIds = [Guid.NewGuid()], StartMinutes = 600, DurationMinutes = 60 };
    private static readonly Mission[] All = [Assault, Recon, Other];

    [Fact]
    public void Nothing_is_proposed_long_before_the_first_mission() =>
        Assert.Null(DiffusionAdvisor.Advise(Alpha, All, null, [], 500));

    [Fact]
    public void First_mission_is_proposed_shortly_before_it_starts()
    {
        var advice = DiffusionAdvisor.Advise(Alpha, All, null, [], 590);
        Assert.Equal(DiffusionAdvice.SendNext, advice!.Advice);
        Assert.Same(Recon, advice.Next);
    }

    [Fact]
    public void Nothing_is_proposed_while_the_published_mission_runs() =>
        Assert.Null(DiffusionAdvisor.Advise(Alpha, All, Recon.Id, [], 640));

    [Fact]
    public void After_its_end_the_orga_is_asked_to_end_it_and_send_the_next()
    {
        var advice = DiffusionAdvisor.Advise(Alpha, All, Recon.Id, [], 665);
        Assert.Equal(DiffusionAdvice.EndCurrent, advice!.Advice);
        Assert.Same(Recon, advice.Current);
        Assert.Same(Assault, advice.Next);
    }

    [Fact]
    public void Completed_and_disabled_missions_are_skipped()
    {
        Assert.Same(Assault, DiffusionAdvisor.Advise(Alpha, All, null, [Recon.Id], 680)!.Next);

        var disabled = new Mission { Name = "Optionnelle", TeamIds = [Alpha], StartMinutes = 680, DurationMinutes = 5, IsEnabled = false };
        Assert.Same(Assault, DiffusionAdvisor.Advise(Alpha, [.. All, disabled], null, [Recon.Id], 680)!.Next);
    }

    [Fact]
    public void Last_mission_ended_proposes_ending_without_next()
    {
        var advice = DiffusionAdvisor.Advise(Alpha, All, Assault.Id, [Recon.Id], 760);
        Assert.Equal(DiffusionAdvice.EndCurrent, advice!.Advice);
        Assert.Null(advice.Next);
        Assert.Null(DiffusionAdvisor.Advise(Alpha, All, null, [Recon.Id, Assault.Id], 760));
    }

    [Fact]
    public void Messages_reach_the_right_teams()
    {
        var faction = Guid.NewGuid();
        var team = new Team { FactionId = faction };
        var other = new Team { FactionId = Guid.NewGuid() };
        Assert.True(new OrgaMessage { Target = MessageTarget.AllTeams }.IsFor(other));
        Assert.True(new OrgaMessage { Target = MessageTarget.Faction, TargetId = faction }.IsFor(team));
        Assert.False(new OrgaMessage { Target = MessageTarget.Faction, TargetId = faction }.IsFor(other));
        Assert.True(new OrgaMessage { Target = MessageTarget.Team, TargetId = team.Id }.IsFor(team));
        Assert.False(new OrgaMessage { Target = MessageTarget.Team, TargetId = team.Id }.IsFor(other));
    }
}
