using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Core.Gps;

namespace AirsoftPlanner.Core.Tests;

public class MessageHistoryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 4, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid Recon = Guid.NewGuid();

    private static OrgaMessage Message(int minutes, MessageKind kind = MessageKind.Text, Guid? mission = null) =>
        new() { SentAt = T0.AddMinutes(minutes), Kind = kind, MissionId = mission, Text = $"m{minutes}" };

    [Fact]
    public void Messages_are_tagged_with_the_mission_running_when_sent()
    {
        var tagged = MessageHistory.TagMissions(
            [Message(30), Message(0), Message(10, MessageKind.MissionAssigned, Recon), Message(20), Message(25, MessageKind.MissionEnded, Recon)],
            id => id == Recon ? "Reconnaissance" : "?");

        Assert.Equal(["", "Reconnaissance", "Reconnaissance", "Reconnaissance", ""], tagged.Select(t => t.Mission));
        Assert.Equal(["m0", "m10", "m20", "m25", "m30"], tagged.Select(t => t.Message.Text));
    }

    [Fact]
    public void Consecutive_messages_of_a_mission_are_grouped_newest_group_first()
    {
        PhoneMessage Phone(int minutes, string mission) => new(Guid.NewGuid(), T0.AddMinutes(minutes), $"m{minutes}", "", MessageKind.Text, Mission: mission);
        var groups = MessageHistory.Group([Phone(0, ""), Phone(10, "Recon"), Phone(20, "Recon"), Phone(30, ""), Phone(40, "Assaut")]);

        Assert.Equal(["Assaut", "", "Recon", ""], groups.Select(g => g.Mission));
        Assert.Equal(["m10", "m20"], groups[2].Messages.Select(m => m.Text));
    }

    [Fact]
    public void Archive_keeps_old_messages_without_duplicates()
    {
        var a = new PhoneMessage(Guid.NewGuid(), T0, "a", "", MessageKind.Text);
        var b = new PhoneMessage(Guid.NewGuid(), T0.AddMinutes(1), "b", "", MessageKind.Text);
        var c = new PhoneMessage(Guid.NewGuid(), T0.AddMinutes(2), "c", "", MessageKind.Text);

        var merged = MessageHistory.Merge([a, b], [b, c]);
        Assert.Equal(["a", "b", "c"], merged.Select(m => m.Text));
        Assert.Equal(["b", "c"], MessageHistory.Merge([a, b], [c], max: 2).Select(m => m.Text));
    }
}
