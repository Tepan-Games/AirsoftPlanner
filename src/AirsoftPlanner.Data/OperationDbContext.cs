using AirsoftPlanner.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace AirsoftPlanner.Data;

public class OperationDbContext(DbContextOptions<OperationDbContext> options) : DbContext(options)
{
    public DbSet<DocumentInfo> DocumentInfo => Set<DocumentInfo>();

    public DbSet<Operation> Operations => Set<Operation>();

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
        modelBuilder.Entity<Operation>().HasQueryFilter(o => !o.IsDeleted);
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
