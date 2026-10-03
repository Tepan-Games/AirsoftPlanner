using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Core.Planning;

namespace AirsoftPlanner.Core.Tests;

public class DelayPlannerTests
{
    private const int End = 18 * 60;
    private static readonly Guid Alpha = Guid.NewGuid();
    private static readonly Guid Bravo = Guid.NewGuid();

    private static Mission M(string name, int start, int duration, bool essential = true, params Guid[] teams) => new()
    {
        Name = name,
        StartMinutes = start,
        DurationMinutes = duration,
        IsEssential = essential,
        TeamIds = [.. teams],
    };

    private static int NewStart(DelayPlan plan, Mission mission) =>
        plan.Changes.FirstOrDefault(c => c.MissionId == mission.Id)?.NewStart ?? mission.StartMinutes;

    [Fact]
    public void Delay_cascades_to_the_next_missions_of_the_same_team()
    {
        var recon = M("Recon", 600, 60, true, Alpha);   // 10:00-11:00
        var assault = M("Assaut", 660, 60, true, Alpha); // 11:00-12:00
        var later = M("Plus tard", 780, 30, true, Alpha); // 13:00, il reste de la marge

        var plan = DelayPlanner.Plan([recon, assault, later], recon.Id, 20, End);

        Assert.Equal(620, NewStart(plan, recon));
        Assert.Equal(680, NewStart(plan, assault));
        Assert.Equal(780, NewStart(plan, later)); // 12:20 < 13:00 : absorbé par la marge
        Assert.Equal(20, plan.EssentialDelayMinutes);
    }

    [Fact]
    public void Delay_cascades_through_prerequisites_across_teams()
    {
        var recon = M("Recon", 600, 60, true, Alpha);
        var exfil = M("Exfiltration", 660, 30, true, Bravo);
        exfil.PredecessorIds.Add(recon.Id);
        var other = M("Indépendante", 660, 30, true, Bravo);

        var plan = DelayPlanner.Plan([recon, exfil, other], recon.Id, 15, End);

        Assert.Equal(675, NewStart(plan, exfil));
        Assert.Equal(660, NewStart(plan, other)); // même équipe, mais chevauchement déjà prévu : pas d'enchaînement à respecter
    }

    [Fact]
    public void Earlier_and_disabled_missions_do_not_move()
    {
        var before = M("Avant", 540, 60, true, Alpha);
        var delayed = M("En retard", 600, 60, true, Alpha);
        var cancelled = M("Annulée", 660, 30, true, Alpha);
        cancelled.IsEnabled = false;

        var plan = DelayPlanner.Plan([before, delayed, cancelled], delayed.Id, 30, End);

        Assert.Equal([delayed.Id], plan.Changes.Select(c => c.MissionId));
    }

    [Fact]
    public void Overflow_beyond_the_end_of_the_operation_is_measured()
    {
        var last = M("Dernière", End - 60, 60, true, Alpha);

        var plan = DelayPlanner.Plan([last], last.Id, 25, End);

        Assert.Equal(25, plan.OverflowMinutes);
    }

    [Fact]
    public void Disabling_an_optional_mission_absorbs_the_delay()
    {
        var recon = M("Recon", 600, 60, true, Alpha);        // 10:00-11:00
        var patrol = M("Patrouille", 660, 45, false, Alpha); // 11:00-11:45, optionnelle
        var assault = M("Assaut", 705, 60, true, Alpha);     // 11:45-12:45
        Mission[] missions = [recon, patrol, assault];

        var plain = DelayPlanner.Plan(missions, recon.Id, 30, End);
        Assert.Equal(735, NewStart(plain, assault)); // poussée de 30 min par la patrouille

        var cut = Assert.Single(DelayPlanner.SuggestCuts(missions, recon.Id, 30, End));
        Assert.Equal(patrol.Id, cut.Mission.Id);
        Assert.Equal(30, cut.GainMinutes);

        var optimized = DelayPlanner.Optimize(missions, recon.Id, 30, End);
        Assert.Equal([patrol.Id], optimized.DisabledMissionIds);
        Assert.Equal(705, NewStart(optimized, assault)); // l'assaut reste à l'heure
        Assert.Equal(0, optimized.EssentialDelayMinutes);
    }

    [Fact]
    public void Optional_missions_that_do_not_help_are_not_suggested()
    {
        var recon = M("Recon", 600, 60, true, Alpha);
        var elsewhere = M("Ailleurs", 700, 30, false, Bravo);

        Assert.Empty(DelayPlanner.SuggestCuts([recon, elsewhere], recon.Id, 30, End));
    }
}
