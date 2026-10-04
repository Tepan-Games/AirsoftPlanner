using AirsoftPlanner.Core.Localization;
using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Core.Gps;
using AirsoftPlanner.Data;

namespace AirsoftPlanner.Core.Tests;

[Collection("Langue")]
public sealed class GameAndReportsTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("airsoft-planner-tests-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Organizer_tracker_is_found_by_its_gps_ids()
    {
        Organizer[] organizers = [new() { Name = "Paul", GpsDeviceIds = "traceur-paul, !a1b2c3d4" }, new() { Name = "Sophie" }];
        Assert.Equal("Paul", GpsParsers.FindOrganizer(organizers, " !A1B2C3D4 ")?.Name);
        Assert.Null(GpsParsers.FindOrganizer(organizers, "Sophie")); // seul le téléphone enrôlé se rattache par le nom
    }

    [Fact]
    public void Resuming_after_a_pause_is_announced_differently_from_the_start()
    {
        L.SetLanguage("fr");
        Assert.Equal("DÉBUT DE PARTIE", GamePhases.AlertTitle(GamePhase.Running, GamePhase.NotStarted));
        Assert.Equal("REPRISE DU JEU", GamePhases.AlertTitle(GamePhase.Running, GamePhase.Paused));
        Assert.Equal("JEU EN PAUSE", GamePhases.AlertTitle(GamePhase.Paused, GamePhase.Running));
        Assert.Equal("FIN DE PARTIE", GamePhases.AlertTitle(GamePhase.Ended, GamePhase.Running));
    }

    [Fact]
    public void Game_phase_and_phone_reports_survive_reopening()
    {
        var path = Path.Combine(_directory, "partie" + OperationFile.Extension);
        var team = Guid.NewGuid();
        using (var file = OperationFile.Create(path, "OP", "Orga"))
        {
            file.Operation.GamePhase = GamePhase.Paused;
            file.Operation.GamePhaseSince = new DateTimeOffset(2026, 10, 3, 14, 32, 0, TimeSpan.Zero);
            file.Add(new GamePhaseEvent { Phase = GamePhase.Running, At = file.Operation.GamePhaseSince.Value.AddHours(-2) });
            file.Add(new GamePhaseEvent { Phase = GamePhase.Paused, At = file.Operation.GamePhaseSince.Value });
            file.Add(new PhoneReport { AuthorId = team, Author = "Alpha", Recipient = MessageSender.Hq, Text = "Objectif atteint",
                Photo = [1, 2, 3], Latitude = 43.6, Longitude = 5.9, ClientId = Guid.NewGuid() });
            file.Add(new Organizer { Name = "Paul", GpsDeviceIds = "traceur-paul" });
            file.Save();
        }

        using var reopened = OperationFile.Open(path);
        Assert.Equal((GamePhase.Paused, new DateTimeOffset(2026, 10, 3, 14, 32, 0, TimeSpan.Zero)),
            (reopened.Operation.GamePhase, reopened.Operation.GamePhaseSince!.Value));
        Assert.Equal([GamePhase.Running, GamePhase.Paused], reopened.LoadGamePhaseEvents().Select(e => e.Phase));
        var report = Assert.Single(reopened.LoadPhoneReports());
        Assert.Equal((team, "Alpha", MessageSender.Hq, "Objectif atteint", 3, 43.6), (report.AuthorId, report.Author, report.Recipient, report.Text, report.Photo!.Length, report.Latitude!.Value));
        Assert.Equal("traceur-paul", Assert.Single(reopened.LoadOrganizers()).GpsDeviceIds);
    }
}
