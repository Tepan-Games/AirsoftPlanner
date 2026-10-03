using AirsoftPlanner.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace AirsoftPlanner.Data;

public class OperationDbContext(DbContextOptions<OperationDbContext> options) : DbContext(options)
{
    public DbSet<DocumentInfo> DocumentInfo => Set<DocumentInfo>();

    public DbSet<Operation> Operations => Set<Operation>();

    public DbSet<Faction> Factions => Set<Faction>();

    public DbSet<Team> Teams => Set<Team>();

    public DbSet<Zone> Zones => Set<Zone>();

    public DbSet<TerrainMap> TerrainMaps => Set<TerrainMap>();

    public DbSet<MapLayer> MapLayers => Set<MapLayer>();

    public DbSet<Mission> Missions => Set<Mission>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        TouchModifiedEntities();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        TouchModifiedEntities();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DocumentInfo>().HasKey(d => d.Id);

        // Les éléments supprimés restent dans le fichier (pour la fusion) mais sont masqués.
        modelBuilder.Entity<Operation>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<Faction>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<Team>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<TerrainMap>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<TerrainMap>().Ignore(t => t.Bounds);
        modelBuilder.Entity<MapLayer>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<MapLayer>().Ignore(l => l.Bounds);
        modelBuilder.Entity<Operation>().Property(o => o.CoordinateFormat).HasConversion<string>();

        var zone = modelBuilder.Entity<Zone>();
        zone.HasQueryFilter(e => !e.IsDeleted);
        zone.Property(z => z.Kind).HasConversion<string>();
        zone.Property(z => z.Points).HasJsonListConversion();

        var mission = modelBuilder.Entity<Mission>();
        mission.HasQueryFilter(e => !e.IsDeleted);
        mission.Ignore(m => m.EndMinutes);
        mission.Property(m => m.TeamIds).HasJsonListConversion();
        mission.Property(m => m.PredecessorIds).HasJsonListConversion();
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite ne sait pas trier les DateTimeOffset : on les stocke en ticks UTC.
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToUtcTicksConverter>();
    }

    private void TouchModifiedEntities()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in ChangeTracker.Entries<Entity>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
                entry.Entity.UpdatedAt = now;
        }
    }
}
