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

    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();

    public DbSet<TeamVehicle> TeamVehicles => Set<TeamVehicle>();

    public DbSet<GameItem> GameItems => Set<GameItem>();

    public DbSet<TeamPosition> TeamPositions => Set<TeamPosition>();

    public DbSet<RuleDocument> RuleDocuments => Set<RuleDocument>();

    public DbSet<TeamPackage> TeamPackages => Set<TeamPackage>();

    public DbSet<ItemEvent> ItemEvents => Set<ItemEvent>();

    public DbSet<PlayerStatusEvent> PlayerStatusEvents => Set<PlayerStatusEvent>();

    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<Expense> Expenses => Set<Expense>();

    public DbSet<TeamAdjustment> TeamAdjustments => Set<TeamAdjustment>();

    public DbSet<VehiclePosition> VehiclePositions => Set<VehiclePosition>();

    public DbSet<Organizer> Organizers => Set<Organizer>();

    public DbSet<OrgaMessage> OrgaMessages => Set<OrgaMessage>();

    public DbSet<AirsoftPlanner.Core.Gps.EnrolledDevice> EnrolledDevices => Set<AirsoftPlanner.Core.Gps.EnrolledDevice>();

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
        mission.Property(m => m.Items).HasJsonListConversion();

        modelBuilder.Entity<TeamMember>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<TeamVehicle>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<GameItem>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<GameItem>().Property(i => i.Category).HasConversion<string>();
        modelBuilder.Entity<TeamPosition>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<TeamPosition>().Ignore(p => p.Point);
        modelBuilder.Entity<TeamPosition>().HasIndex(p => new { p.TeamId, p.ReceivedAt });
        modelBuilder.Entity<Operation>().Property(o => o.WalkingSpeedKmh).HasDefaultValue(3.0);
        modelBuilder.Entity<RuleDocument>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<RuleDocument>().Ignore(r => r.IsImported);
        modelBuilder.Entity<TeamPackage>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<Team>().Property(t => t.Status).HasConversion<string>();
        modelBuilder.Entity<ItemEvent>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<ItemEvent>().Property(e => e.Kind).HasConversion<string>();
        modelBuilder.Entity<ItemEvent>().Ignore(e => e.Location);
        modelBuilder.Entity<PlayerStatusEvent>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<PlayerStatusEvent>().Property(e => e.Reason).HasConversion<string>();
        modelBuilder.Entity<Payment>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<Payment>().Property(p => p.Method).HasConversion<string>();
        modelBuilder.Entity<Expense>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<Expense>().Property(e => e.Category).HasConversion<string>();
        modelBuilder.Entity<TeamAdjustment>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<TeamAdjustment>().Property(a => a.Kind).HasConversion<string>();
        modelBuilder.Entity<VehiclePosition>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<VehiclePosition>().Ignore(p => p.Point);
        modelBuilder.Entity<AirsoftPlanner.Core.Gps.EnrolledDevice>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<AirsoftPlanner.Core.Gps.EnrolledDevice>().HasIndex(d => d.Token);
        modelBuilder.Entity<Operation>().Property(o => o.TrackingIntervalSeconds).HasDefaultValue(30);
        modelBuilder.Entity<Operation>().Property(o => o.AllyShareMode).HasConversion<string>();
        modelBuilder.Entity<Operation>().Property(o => o.VehicleSpeedKmh).HasDefaultValue(25.0);
        modelBuilder.Entity<Organizer>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<Operation>().Property(o => o.IgnoredRadioConflicts).HasJsonListConversion();
        modelBuilder.Entity<Team>().Property(t => t.CompletedMissionIds).HasJsonListConversion();
        var message = modelBuilder.Entity<OrgaMessage>();
        message.HasQueryFilter(e => !e.IsDeleted);
        message.Property(m => m.Target).HasConversion<string>();
        message.Property(m => m.Kind).HasConversion<string>();
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite ne sait pas trier les DateTimeOffset : on les stocke en ticks UTC.
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToUtcTicksConverter>();
    }

    /// <summary>Vrai pendant une fusion : les dates de modification d'origine sont conservées.</summary>
    public bool PreserveTimestamps { get; set; }

    private void TouchModifiedEntities()
    {
        if (PreserveTimestamps)
            return;

        var now = DateTimeOffset.UtcNow;
        foreach (var entry in ChangeTracker.Entries<Entity>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
                entry.Entity.UpdatedAt = now;
        }
    }
}
