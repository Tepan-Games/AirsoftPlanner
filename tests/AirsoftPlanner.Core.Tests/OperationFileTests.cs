using AirsoftPlanner.Data;

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
    public void Open_rejects_a_file_that_is_not_an_operation()
    {
        var path = Path.Combine(_directory, "faux" + OperationFile.Extension);
        File.WriteAllText(path, "pas une base de données");

        Assert.Throws<InvalidDataException>(() => OperationFile.Open(path));
    }
}
