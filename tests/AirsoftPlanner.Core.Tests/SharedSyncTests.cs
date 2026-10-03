using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Data;

namespace AirsoftPlanner.Core.Tests;

public sealed class SharedSyncTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("airsoft-planner-sync-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Local(string name) => Path.Combine(_directory, name + OperationFile.Extension);

    private string Shared => Path.Combine(_directory, "OneDrive", "OP Tempête" + OperationFile.Extension);

    [Fact]
    public void Two_organizers_working_at_the_same_time_see_each_other_changes()
    {
        // Orga 1 crée l'OP et la partage.
        using (var orga1 = OperationFile.Create(Local("orga1"), "OP Tempête", "Orga 1"))
        {
            orga1.Add(new Team { Name = "Alpha" });
            SharedSync.Sync(orga1, Shared);
        }

        // Orga 2 récupère sa copie de travail depuis le dossier partagé.
        File.Copy(Shared, Local("orga2"));

        // Chacun travaille de son côté, en même temps.
        using (var orga1 = OperationFile.Open(Local("orga1")))
        {
            orga1.Add(new Team { Name = "Bravo" });
            SharedSync.Sync(orga1, Shared);
        }

        using (var orga2 = OperationFile.Open(Local("orga2")))
        {
            orga2.Add(new Mission { Name = "Assaut" });
            var report = SharedSync.Sync(orga2, Shared);
            Assert.Equal(1, report.Received.Added); // Bravo, ajoutée par l'orga 1
            Assert.Equal(["Alpha", "Bravo"], orga2.LoadTeams().Select(t => t.Name));
        }

        // Orga 1 reçoit la mission de l'orga 2 à sa synchronisation suivante.
        using (var orga1 = OperationFile.Open(Local("orga1")))
        {
            SharedSync.Sync(orga1, Shared);
            Assert.Equal("Assaut", Assert.Single(orga1.LoadMissions()).Name);
        }
    }

    [Fact]
    public void Conflict_copies_left_by_the_sync_service_are_merged_and_removed()
    {
        using (var orga1 = OperationFile.Create(Local("orga1"), "OP Tempête", "Orga 1"))
            SharedSync.Sync(orga1, Shared);

        // OneDrive a gardé les deux versions : « OP Tempête-PC-PAUL.aop » contient l'équipe de Paul.
        var conflict = Path.Combine(Path.GetDirectoryName(Shared)!, "OP Tempête-PC-PAUL" + OperationFile.Extension);
        File.Copy(Shared, conflict);
        using (var paul = OperationFile.Open(conflict))
        {
            paul.Add(new Team { Name = "Équipe de Paul" });
            paul.Save();
        }

        // Fichier d'une autre OP dans le même dossier : ne doit pas être touché.
        var otherOp = Path.Combine(Path.GetDirectoryName(Shared)!, "OP Tempête 2027" + OperationFile.Extension);
        using (OperationFile.Create(otherOp, "OP Tempête 2027", "Orga")) { }

        using (var orga1 = OperationFile.Open(Local("orga1")))
        {
            var report = SharedSync.Sync(orga1, Shared);
            Assert.Equal(1, report.ConflictCopiesMerged);
            Assert.Equal("Équipe de Paul", Assert.Single(orga1.LoadTeams()).Name);
        }

        Assert.False(File.Exists(conflict));
        Assert.True(File.Exists(otherOp));
        using var shared = OperationFile.Open(Shared);
        Assert.Equal("Équipe de Paul", Assert.Single(shared.LoadTeams()).Name);
    }
}
