using AirsoftPlanner.Core.Documents;
using AirsoftPlanner.Core.Domain;

namespace AirsoftPlanner.Core.Tests;

public class PackageTests
{
    private readonly Team _team = new() { Name = "Alpha", RadioFrequency = "PMR 2" };
    private readonly Faction _faction = new() { Name = "OTAN", RadioFrequency = "PMR 1" };
    private readonly Zone _zone = new() { Name = "Village", Points = [new GeoPoint(45, 5)] };
    private readonly RuleDocument _rules = new() { Title = "Règles générales", Text = "# Sécurité\nLunettes obligatoires." };
    private readonly Mission _mission;

    public PackageTests() =>
        _mission = new Mission { Name = "Assaut", StartMinutes = 600, DurationMinutes = 60, TeamIds = [_team.Id], ZoneId = _zone.Id };

    private string Fingerprint() => PackageFingerprint.Compute(_team, _faction, [_mission],
        new Dictionary<Guid, Zone> { [_zone.Id] = _zone }, [_rules]);

    [Fact]
    public void Fingerprint_is_stable_for_the_same_content()
    {
        Assert.Equal(Fingerprint(), Fingerprint());
    }

    [Fact]
    public void Fingerprint_changes_when_anything_sent_to_the_team_changes()
    {
        var changes = new Action[]
        {
            () => _mission.StartMinutes = 630,
            () => _mission.Description = "Nouvelles consignes",
            () => _zone.Points = [new GeoPoint(45.001, 5)],
            () => _rules.Text += "\nTir à la tête interdit.",
            () => _faction.RadioFrequency = "PMR 4",
            () => _team.RadioFrequency = "PMR 6",
            () => _mission.IsEnabled = false,
        };

        var seen = new HashSet<string> { Fingerprint() };
        foreach (var change in changes)
        {
            change();
            Assert.True(seen.Add(Fingerprint()), "L'empreinte aurait dû changer.");
        }
    }

    [Fact]
    public void Imported_rule_files_are_compared_by_content()
    {
        var file = new RuleDocument { Title = "Charte du terrain", FileName = "charte.pdf", FileContent = [1, 2, 3] };
        string Compute() => PackageFingerprint.Compute(_team, null, [], new Dictionary<Guid, Zone>(), [file]);

        var before = Compute();
        file.FileContent = [1, 2, 4];
        Assert.NotEqual(before, Compute());
    }

    [Fact]
    public void Status_follows_generation_sending_and_confirmation()
    {
        var current = Fingerprint();
        var package = new TeamPackage { TeamId = _team.Id };

        Assert.Equal(PackageStatus.NotGenerated, PackageState.Of(null, current));
        Assert.Equal(PackageStatus.NotGenerated, PackageState.Of(package, current));

        package.GeneratedAt = DateTimeOffset.Now;
        package.Fingerprint = current;
        Assert.Equal(PackageStatus.ReadyToSend, PackageState.Of(package, current));

        package.SentAt = DateTimeOffset.Now;
        Assert.Equal(PackageStatus.AwaitingConfirmation, PackageState.Of(package, current));

        package.ReceivedAt = DateTimeOffset.Now;
        Assert.Equal(PackageStatus.Received, PackageState.Of(package, current));

        _mission.StartMinutes += 15;
        Assert.Equal(PackageStatus.Outdated, PackageState.Of(package, Fingerprint()));
    }
}
