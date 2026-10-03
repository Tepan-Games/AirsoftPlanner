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

    /// <summary>
    /// Historique : 2 = factions, équipes, zones, fonds de carte ; 3 = missions.
    /// Les fichiers d'une version précédente sont mis à niveau à l'ouverture.
    /// </summary>
    public const int CurrentFormatVersion = 3;

    /// <summary>Les fichiers plus anciens viennent de préversions de développement et ne sont pas repris.</summary>
    public const int MinimumFormatVersion = 2;

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
        file.Context.TerrainMaps.Add(new TerrainMap());
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
            if (info.FormatVersion < MinimumFormatVersion)
                throw new InvalidDataException(
                    "Ce fichier a été créé avec une préversion d'Airsoft Planner et ne peut plus être ouvert.");
            if (info.FormatVersion < CurrentFormatVersion)
            {
                SchemaUpgrader.Upgrade(context);
                context.Database.ExecuteSql($"UPDATE DocumentInfo SET FormatVersion = {CurrentFormatVersion}");
            }
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

    public TerrainMap TerrainMap => Context.TerrainMaps.Single();

    public IReadOnlyList<Faction> LoadFactions() => Context.Factions.OrderBy(f => f.Name).ToList();

    public IReadOnlyList<Team> LoadTeams() => Context.Teams.OrderBy(t => t.Name).ToList();

    public IReadOnlyList<Zone> LoadZones() => Context.Zones.OrderBy(z => z.Name).ToList();

    public IReadOnlyList<Mission> LoadMissions() => Context.Missions.OrderBy(m => m.StartMinutes).ThenBy(m => m.Name).ToList();

    public IReadOnlyList<MapLayer> LoadMapLayers() => Context.MapLayers.OrderBy(l => l.SortOrder).ToList();

    public void Add(Entity entity) => Context.Add(entity);

    /// <summary>
    /// Supprime un élément. Un élément jamais enregistré disparaît ; sinon il est seulement
    /// marqué supprimé, pour que la suppression puisse être reportée lors d'une fusion.
    /// </summary>
    public void Remove(Entity entity)
    {
        if (Context.Entry(entity).State == EntityState.Added)
            Context.Entry(entity).State = EntityState.Detached;
        else
            entity.IsDeleted = true;
    }

    public bool HasUnsavedChanges
    {
        get
        {
            Context.ChangeTracker.DetectChanges();
            return Context.ChangeTracker.HasChanges();
        }
    }

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
