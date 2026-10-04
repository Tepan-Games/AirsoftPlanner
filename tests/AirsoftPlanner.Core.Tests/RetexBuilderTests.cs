using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Core.Retex;

namespace AirsoftPlanner.Core.Tests;

public class RetexBuilderTests
{
    private static readonly DateTimeOffset Day = new(2026, 10, 3, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Team_sheet_summarises_missions_distance_strength_and_messages()
    {
        var alpha = new Team { Name = "Alpha" };
        var bravo = new Team { Name = "Bravo" };
        var recon = new Mission { Name = "Recon", TeamIds = [alpha.Id], StartMinutes = 600, DurationMinutes = 60 };
        var assault = new Mission { Name = "Assaut", TeamIds = [alpha.Id], StartMinutes = 690, DurationMinutes = 60 };
        alpha.CompletedMissionIds = [recon.Id];
        var messages = new List<OrgaMessage>
        {
            new() { Kind = MessageKind.MissionAssigned, MissionId = recon.Id, Target = MessageTarget.Team, TargetId = alpha.Id, SentAt = Day.AddMinutes(605), Text = "Recon" },
            new() { Kind = MessageKind.MissionAssigned, MissionId = assault.Id, Target = MessageTarget.Team, TargetId = alpha.Id, SentAt = Day.AddMinutes(670), Text = "Assaut" },
            new() { Target = MessageTarget.Team, TargetId = bravo.Id, SentAt = Day.AddMinutes(620), Text = "Pour Bravo" },
            new() { Target = MessageTarget.AllTeams, Sender = MessageSender.Orga, SentAt = Day.AddMinutes(700), Text = "Fin à 17 h" },
        };
        var positions = new List<TeamPosition>
        {
            new() { TeamId = alpha.Id, Latitude = 43.6400, Longitude = 6.0000, ReceivedAt = Day.AddMinutes(600) },
            new() { TeamId = alpha.Id, Latitude = 43.6490, Longitude = 6.0000, ReceivedAt = Day.AddMinutes(610) }, // ≈ 1 km
            new() { TeamId = alpha.Id, Latitude = 44.0000, Longitude = 6.0000, ReceivedAt = Day.AddMinutes(611) }, // saut GPS ignoré
        };
        var players = new List<PlayerStatusEvent> { new() { TeamId = alpha.Id, IsOut = true, Players = 2, Reason = OutReason.Rest, At = Day.AddMinutes(650) } };

        var retex = RetexBuilder.Build([alpha, bravo], [recon, assault], positions, messages, [], players,
            m => Day.AddMinutes(m), _ => "?", k => k.ToString(), r => r.ToString());

        var sheet = retex.Teams[0];
        Assert.Equal((2, 2, 1), (sheet.MissionsPlanned, sheet.MissionsPublished, sheet.MissionsCompleted));
        Assert.Equal(5, sheet.Missions[0].DiffusionDelayMinutes);
        Assert.Equal(65, sheet.Missions[0].ActualMinutes); // terminée par la diffusion de l'assaut
        Assert.InRange(sheet.DistanceKm, 0.95, 1.05);
        Assert.Equal(2, sheet.PlayersOut);
        Assert.Equal(["Recon", "Assaut", "Fin à 17 h"], sheet.Messages.Select(m => m.Text));
        Assert.DoesNotContain(sheet.Events, e => e.Text == "Pour Bravo");
        Assert.Equal(5, retex.Timeline.Count);
        Assert.Equal("Toutes les équipes", retex.Timeline.Single(e => e.Text == "Fin à 17 h").Team);
    }
}
