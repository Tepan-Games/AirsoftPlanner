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
    /// Historique : 2 = factions, équipes, zones, fonds de carte ; 3 = missions ;
    /// 4 = membres, véhicules, radio, matériel de jeu, positions des équipes ; 5 = règles et packages ;
    /// 6 = statut d'inscription des équipes ; 7 = suivi des objets d'objectif ;
    /// 8 = joueurs hors jeu ; 9 = finances (tarif, paiements, dépenses) ;
    /// 10 = identifiants GPS des équipes ; 11 = remises, cadeaux, véhicules en jeu et leurs traces ;
    /// 12 = enrôlement de l'application Android.
    /// Les fichiers d'une version précédente sont mis à niveau à l'ouverture.
    /// </summary>
    public const int CurrentFormatVersion = 22;

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
                if (info.FormatVersion < 21)
                {
                    // Niveaux de difficulté : repris de l'ancien partage des positions (carte → Facile, sinon Moyen).
                    context.Database.ExecuteSql($"UPDATE Operations SET HqDifficulty = CASE AllyShareMode WHEN 'Map' THEN 'Easy' ELSE 'Medium' END");
                }

                if (info.FormatVersion < 22)
                {
                    // Résultat des missions : barème par défaut (pas de HasDefaultValue, qui remplacerait un 0 choisi par l'orga).
                    context.Database.ExecuteSql($"UPDATE Missions SET SuccessPoints = 10, PartialPoints = 5");
                }
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

    public IReadOnlyList<TeamMember> LoadMembers() => Context.TeamMembers.OrderBy(m => m.SortOrder).ToList();

    public IReadOnlyList<TeamVehicle> LoadVehicles() => Context.TeamVehicles.OrderBy(v => v.SortOrder).ToList();

    public IReadOnlyList<RuleDocument> LoadRuleDocuments() => Context.RuleDocuments.OrderBy(r => r.SortOrder).ToList();

    public IReadOnlyList<OrgaMessage> LoadMessages() => Context.OrgaMessages.OrderBy(m => m.SentAt).ToList();

    public IReadOnlyList<Organizer> LoadOrganizers() => Context.Organizers.OrderBy(o => o.SortOrder).ToList();

    public IReadOnlyList<AirsoftPlanner.Core.Gps.EnrolledDevice> LoadEnrolledDevices() => Context.EnrolledDevices.ToList();

    public IReadOnlyList<TeamAdjustment> LoadAdjustments() => Context.TeamAdjustments.ToList();

    public IReadOnlyList<VehiclePosition> LoadVehiclePositions() => Context.VehiclePositions.AsEnumerable().OrderBy(p => p.At).ToList();

    public IReadOnlyList<Payment> LoadPayments() => Context.Payments.AsEnumerable().OrderBy(p => p.Date).ToList();

    public IReadOnlyList<Expense> LoadExpenses() => Context.Expenses.AsEnumerable().OrderBy(e => e.Date).ToList();

    public IReadOnlyList<PlayerStatusEvent> LoadPlayerStatusEvents() => Context.PlayerStatusEvents.AsEnumerable().OrderBy(e => e.At).ToList();

    public IReadOnlyList<ItemEvent> LoadItemEvents() => Context.ItemEvents.AsEnumerable().OrderBy(e => e.At).ToList();

    public IReadOnlyList<TeamPackage> LoadTeamPackages() => Context.TeamPackages.ToList();

    public IReadOnlyList<GameItem> LoadGameItems() => Context.GameItems.OrderBy(i => i.Name).ToList();

    /// <summary>Toutes les positions reçues, par ordre chronologique.</summary>
    public IReadOnlyList<TeamPosition> LoadPositions() => Context.TeamPositions.AsEnumerable().OrderBy(p => p.ReceivedAt).ToList();

    /// <summary>Dernière position reçue de chaque équipe.</summary>
    public IReadOnlyDictionary<Guid, TeamPosition> LoadLastPositions() => Context.TeamPositions
        .AsEnumerable()
        .GroupBy(p => p.TeamId)
        .ToDictionary(g => g.Key, g => g.MaxBy(p => p.ReceivedAt)!);

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
