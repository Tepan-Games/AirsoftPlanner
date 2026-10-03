using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Data;
using Microsoft.EntityFrameworkCore;

namespace AirsoftPlanner.Core.Tests;

public sealed class OperationFileTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("airsoft-planner-tests-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Create_then_reopen_keeps_operation()
    {
        var path = Path.Combine(_directory, "op" + OperationFile.Extension);

        using (var file = OperationFile.Create(path, "Opération Tempête", "Orga"))
        {
            file.Operation.Location = "Terrain du Bois";
            file.Save();
        }

        using var reopened = OperationFile.Open(path);
        Assert.Equal("Opération Tempête", reopened.Operation.Name);
        Assert.Equal("Terrain du Bois", reopened.Operation.Location);
    }

    [Fact]
    public void Closed_file_can_be_copied_and_opened_elsewhere()
    {
        var path = Path.Combine(_directory, "op" + OperationFile.Extension);
        var copy = Path.Combine(_directory, "copie" + OperationFile.Extension);
        Guid operationId;

        using (var file = OperationFile.Create(path, "OP partagée", "Orga 1"))
            operationId = file.Operation.Id;

        File.Copy(path, copy);
        File.Delete(path);

        using var shared = OperationFile.Open(copy);
        Assert.Equal(operationId, shared.Operation.Id);
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public void Factions_teams_zones_and_terrain_survive_reopening()
    {
        var path = Path.Combine(_directory, "op" + OperationFile.Extension);

        using (var file = OperationFile.Create(path, "OP", "Orga"))
        {
            var red = new Faction { Name = "Rouges", Color = "#C62828" };
            file.Add(red);
            file.Add(new Team { Name = "Alpha", FactionId = red.Id, PlayerCount = 8 });
            file.Add(new Zone
            {
                Name = "Village",
                Kind = ZoneKind.Area,
                Points = [new GeoPoint(45.1, 5.1), new GeoPoint(45.1, 5.2), new GeoPoint(45.0, 5.2)],
            });
            file.TerrainMap.Bounds = new GeoBounds(45.2, 45.0, 5.0, 5.3);
            file.Add(new MapLayer { Name = "Photo aérienne", Image = [1, 2, 3], Bounds = new GeoBounds(45.2, 45.0, 5.0, 5.3) });
            file.Operation.CoordinateFormat = AirsoftPlanner.Core.Geo.CoordinateFormat.DegreesMinutesSeconds;
            file.Save();
        }

        using var reopened = OperationFile.Open(path);
        var faction = Assert.Single(reopened.LoadFactions());
        var team = Assert.Single(reopened.LoadTeams());
        var zone = Assert.Single(reopened.LoadZones());
        Assert.Equal(faction.Id, team.FactionId);
        Assert.Equal(3, zone.Points.Count);
        Assert.Equal(new GeoPoint(45.0, 5.2), zone.Points[2]);
        Assert.Equal(45.2, reopened.TerrainMap.North);
        var layer = Assert.Single(reopened.LoadMapLayers());
        Assert.Equal([1, 2, 3], layer.Image);
        Assert.Equal(5.3, layer.East);
        Assert.Equal(AirsoftPlanner.Core.Geo.CoordinateFormat.DegreesMinutesSeconds, reopened.Operation.CoordinateFormat);
    }

    [Fact]
    public void Editing_zone_points_in_place_is_saved()
    {
        var path = Path.Combine(_directory, "op" + OperationFile.Extension);

        using (var file = OperationFile.Create(path, "OP", "Orga"))
        {
            file.Add(new Zone { Name = "Pont", Kind = ZoneKind.Point });
            file.Save();
            file.LoadZones()[0].Points.Add(new GeoPoint(44, 4));
            Assert.True(file.HasUnsavedChanges);
            file.Save();
        }

        using var reopened = OperationFile.Open(path);
        Assert.Equal([new GeoPoint(44, 4)], reopened.LoadZones()[0].Points);
    }

    [Fact]
    public void Removed_items_are_hidden_but_kept_in_the_file_for_merging()
    {
        var path = Path.Combine(_directory, "op" + OperationFile.Extension);

        using (var file = OperationFile.Create(path, "OP", "Orga"))
        {
            var kept = new Team { Name = "Gardée" };
            var removed = new Team { Name = "Supprimée" };
            var neverSaved = new Team { Name = "Jamais enregistrée" };
            file.Add(kept);
            file.Add(removed);
            file.Save();
            file.Add(neverSaved);
            file.Remove(removed);
            file.Remove(neverSaved);
            file.Save();
        }

        using var reopened = OperationFile.Open(path);
        Assert.Equal(["Gardée"], reopened.LoadTeams().Select(t => t.Name));
        Assert.Equal(2, reopened.Context.Teams.IgnoreQueryFilters().Count());
    }

    [Fact]
    public void Missions_survive_reopening()
    {
        var path = Path.Combine(_directory, "op" + OperationFile.Extension);
        var alpha = Guid.NewGuid();
        var first = new Mission { Name = "Reconnaissance", TeamIds = [alpha], StartMinutes = 600, DurationMinutes = 45 };

        using (var file = OperationFile.Create(path, "OP", "Orga"))
        {
            file.Add(first);
            file.Add(new Mission { Name = "Assaut", TeamIds = [alpha], StartMinutes = 660, IsEssential = false, PredecessorIds = [first.Id] });
            file.Save();
        }

        using var reopened = OperationFile.Open(path);
        var missions = reopened.LoadMissions();
        Assert.Equal(["Reconnaissance", "Assaut"], missions.Select(m => m.Name));
        Assert.Equal([alpha], missions[0].TeamIds);
        Assert.Equal(645, missions[0].EndMinutes);
        Assert.False(missions[1].IsEssential);
        Assert.Equal([first.Id], missions[1].PredecessorIds);
    }

    [Fact]
    public void Files_from_the_previous_format_are_upgraded_on_open()
    {
        var path = Path.Combine(_directory, "ancien" + OperationFile.Extension);
        using (var file = OperationFile.Create(path, "OP v2", "Orga"))
        {
            file.Add(new Team { Name = "Alpha", Notes = "à garder" });
            file.Save();
            // Simule un fichier de la version 2 : pas de table des missions ni de colonne Notes.
            file.Context.Database.ExecuteSqlRaw("DROP TABLE Missions");
            file.Context.Database.ExecuteSqlRaw("ALTER TABLE Teams DROP COLUMN Notes");
            file.Context.Database.ExecuteSqlRaw("UPDATE DocumentInfo SET FormatVersion = 2");
        }

        using (var upgraded = OperationFile.Open(path))
        {
            var team = Assert.Single(upgraded.LoadTeams());
            Assert.Equal("Alpha", team.Name);
            Assert.Equal("", team.Notes);
            Assert.Empty(upgraded.LoadMissions());
            upgraded.Add(new Mission { Name = "Nouvelle", TeamIds = [team.Id] });
            upgraded.Save();
            Assert.Equal(OperationFile.CurrentFormatVersion, upgraded.Context.DocumentInfo.Single().FormatVersion);
        }

        using var reopened = OperationFile.Open(path);
        Assert.Equal("Nouvelle", Assert.Single(reopened.LoadMissions()).Name);
    }

    [Fact]
    public void Open_rejects_a_file_that_is_not_an_operation()
    {
        var path = Path.Combine(_directory, "faux" + OperationFile.Extension);
        File.WriteAllText(path, "pas une base de données");

        Assert.Throws<InvalidDataException>(() => OperationFile.Open(path));
    }
}
