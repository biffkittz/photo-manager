using Microsoft.EntityFrameworkCore;
using PhotoManager.Core.Crypto;
using PhotoManager.Core.Domain;
using PhotoManager.Data.Entities;

namespace PhotoManager.Data;

/// <summary>EF Core context for the PostgreSQL metadata database.</summary>
/// <param name="options">Context options (see <see cref="PhotoDbContextOptions"/>).</param>
/// <param name="timeProvider">Clock used to stamp <see cref="ITimestamped"/> entities.</param>
public sealed class PhotoDbContext(DbContextOptions<PhotoDbContext> options, TimeProvider? timeProvider = null)
    : DbContext(options)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    /// <summary>Gets the photos.</summary>
    public DbSet<Photo> Photos => Set<Photo>();

    /// <summary>Gets the maintenance jobs.</summary>
    public DbSet<MaintenanceJob> Jobs => Set<MaintenanceJob>();

    /// <summary>Gets the prepared downloads.</summary>
    public DbSet<PreparedDownload> Downloads => Set<PreparedDownload>();

    /// <summary>Gets the audit trail.</summary>
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    /// <inheritdoc />
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampTimestamps();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <inheritdoc />
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampTimestamps();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        // Enums are stored as strings: readable in psql, and reordering members can't corrupt data.
        configurationBuilder.Properties<PhotoStatus>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<PhotoSource>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<ProcessingState>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<DescriptionSource>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<JobKind>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<JobStatus>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<DownloadStatus>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<EncryptionFormat>().HaveConversion<string>().HaveMaxLength(32);
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PhotoDbContext).Assembly);
    }

    private void StampTimestamps()
    {
        // timestamp with time zone stores an instant, and Npgsql refuses to write a DateTimeOffset with a non-zero
        // offset (an EXIF capture time keeps the camera's). Every value is normalized to the same instant in UTC.
        foreach (var entry in ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified))
        {
            foreach (var property in entry.Properties)
            {
                if (property.CurrentValue is DateTimeOffset { Offset.Ticks: not 0 } value)
                {
                    property.CurrentValue = value.ToUniversalTime();
                }
            }
        }

        var now = _timeProvider.GetUtcNow();
        foreach (var photo in ChangeTracker.Entries<Photo>().Where(e => e.State is EntityState.Added or EntityState.Modified))
        {
            photo.Entity.RefreshSearchKeywords();
        }

        foreach (var entry in ChangeTracker.Entries<ITimestamped>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }
    }
}
