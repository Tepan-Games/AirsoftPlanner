using AirsoftPlanner.Core.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AirsoftPlanner.Data;

/// <summary>
/// Un fichier d'OP (*.aop) : une base SQLite autonome contenant toute l'opération.
/// Le fichier peut être copié et transmis tel quel (mail, clé USB, Drive...).
/// </summary>
public sealed class OperationFile : IDisposable
{
    public const string Extension = ".aop";

    public const int CurrentFormatVersion = 1;

    private OperationFile(string path, OperationDbContext context)
    {
        Path = path;
        Context = context;
    }

    public string Path { get; }

    public OperationDbContext Context { get; }

    public static OperationFile Create(string path, string operationName, string createdBy)
    {
        if (File.Exists(path))
            File.Delete(path);

        var file = new OperationFile(path, CreateContext(path));
        file.Context.Database.EnsureCreated();
        file.Context.DocumentInfo.Add(new DocumentInfo
        {
            FormatVersion = CurrentFormatVersion,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = createdBy,
        });
        var today = DateTimeOffset.Now.Date;
        file.Context.Operations.Add(new Operation
        {
            Name = operationName,
            StartsAt = today.AddHours(9),
            EndsAt = today.AddHours(18),
        });
        file.Context.SaveChanges();
        return file;
    }

    public static OperationFile Open(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Fichier d'OP introuvable.", path);

        var context = CreateContext(path);
        try
        {
            var info = context.DocumentInfo.AsNoTracking().SingleOrDefault()
                ?? throw new InvalidDataException("Ce fichier n'est pas un fichier d'OP valide.");
            if (info.FormatVersion > CurrentFormatVersion)
                throw new InvalidDataException(
                    "Ce fichier a été créé avec une version plus récente d'Airsoft Planner. Mettez le logiciel à jour.");
        }
        catch (SqliteException ex)
        {
            context.Dispose();
            throw new InvalidDataException("Ce fichier n'est pas un fichier d'OP valide.", ex);
        }
        catch
        {
            context.Dispose();
            throw;
        }

        return new OperationFile(path, context);
    }

    public Operation Operation => Context.Operations.Single();

    public void Save() => Context.SaveChanges();

    public void Dispose()
    {
        Context.Dispose();
        // Libère le verrou SQLite pour que le fichier puisse être copié ou envoyé.
        SqliteConnection.ClearAllPools();
    }

    private static OperationDbContext CreateContext(string path)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString();
        var options = new DbContextOptionsBuilder<OperationDbContext>().UseSqlite(connectionString).Options;
        return new OperationDbContext(options);
    }
}
