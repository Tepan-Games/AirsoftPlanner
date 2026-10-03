using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Data;
using Microsoft.EntityFrameworkCore;

namespace AirsoftPlanner.Core.Tests;

public sealed class OperationMergerTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("airsoft-planner-merge-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string PathOf(string name) => Path.Combine(_directory, name + OperationFile.Extension);

    [Fact]
    public void Changes_made_in_parallel_on_two_copies_are_combined()
    {
        Guid alphaId, bravoId, charlieId;
        using (var original = OperationFile.Create(PathOf("orga1"), "OP Tempête", "Orga 1"))
        {
            var alpha = new Team { Name = "Alpha" };
            var bravo = new Team { Name = "Bravo" };
            var charlie = new Team { Name = "Charlie" };
            original.Add(alpha);
            original.Add(bravo);
            original.Add(charlie);
            original.Save();
            (alphaId, bravoId, charlieId) = (alpha.Id, bravo.Id, charlie.Id);
        }

        File.Copy(PathOf("orga1"), PathOf("orga2"));

        // Orga 1 : renomme Alpha, ajoute une équipe.
        using (var orga1 = OperationFile.Open(PathOf("orga1")))
        {
            orga1.LoadTeams().Single(t => t.Id == alphaId).Name = "Alpha (orga 1)";
            orga1.Add(new Team { Name = "Delta" });
            orga1.Save();
        }

        Thread.Sleep(20); // modifications de l'orga 2 strictement plus récentes

        // Orga 2 : ajoute une mission, supprime Bravo, renomme Charlie et (plus tard) Alpha.
        using (var orga2 = OperationFile.Open(PathOf("orga2")))
        {
            orga2.Add(new Mission { Name = "Assaut", TeamIds = [charlieId] });
            orga2.Remove(orga2.LoadTeams().Single(t => t.Id == bravoId));
            orga2.LoadTeams().Single(t => t.Id == charlieId).Name = "Charlie (orga 2)";
            orga2.LoadTeams().Single(t => t.Id == alphaId).Name = "Alpha (orga 2)";
            orga2.Save();
        }

        MergeReport report;
        using (var orga1 = OperationFile.Open(PathOf("orga1")))
            report = OperationMerger.Merge(orga1, PathOf("orga2"));

        using var merged = OperationFile.Open(PathOf("orga1"));
        Assert.Equal(["Alpha (orga 2)", "Charlie (orga 2)", "Delta"], merged.LoadTeams().Select(t => t.Name).Order());
        Assert.Equal("Assaut", Assert.Single(merged.LoadMissions()).Name);
        Assert.Equal(1, report.Added);   // la mission
        Assert.Equal(1, report.Deleted); // Bravo
        Assert.True(report.Updated >= 2); // Alpha, Charlie
    }

    [Fact]
    public void Merging_twice_changes_nothing_the_second_time()
    {
        using (var original = OperationFile.Create(PathOf("a"), "OP", "Orga"))
        {
            original.Add(new Team { Name = "Alpha" });
            original.Save();
        }

        File.Copy(PathOf("a"), PathOf("b"));
        using (var b = OperationFile.Open(PathOf("b")))
        {
            b.Add(new Zone { Name = "Village" });
            b.Save();
        }

        using (var a = OperationFile.Open(PathOf("a")))
            Assert.Equal(1, OperationMerger.Merge(a, PathOf("b")).Changes);
        using (var a = OperationFile.Open(PathOf("a")))
            Assert.Equal(0, OperationMerger.Merge(a, PathOf("b")).Changes);
    }

    [Fact]
    public void Merge_keeps_the_original_modification_dates()
    {
        using (var original = OperationFile.Create(PathOf("a"), "OP", "Orga"))
            original.Save();
        File.Copy(PathOf("a"), PathOf("b"));
        DateTimeOffset written;
        using (var b = OperationFile.Open(PathOf("b")))
        {
            var team = new Team { Name = "Alpha" };
            b.Add(team);
            b.Save();
            written = team.UpdatedAt;
        }

        Thread.Sleep(20);
        using (var a = OperationFile.Open(PathOf("a")))
            OperationMerger.Merge(a, PathOf("b"));

        using var merged = OperationFile.Open(PathOf("a"));
        Assert.Equal(written, merged.LoadTeams().Single().UpdatedAt);
    }

    [Fact]
    public void Different_operations_cannot_be_merged()
    {
        using (OperationFile.Create(PathOf("op1"), "OP 1", "Orga")) { }
        using (OperationFile.Create(PathOf("op2"), "OP 2", "Orga")) { }

        using var op1 = OperationFile.Open(PathOf("op1"));
        var error = Assert.Throws<InvalidDataException>(() => OperationMerger.Merge(op1, PathOf("op2")));
        Assert.Contains("autre OP", error.Message);
    }
}
