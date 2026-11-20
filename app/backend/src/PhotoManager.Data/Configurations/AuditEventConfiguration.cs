using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoManager.Data.Entities;

namespace PhotoManager.Data.Configurations;

/// <summary>Table and indexes for <see cref="AuditEvent"/>.</summary>
internal sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("audit_events");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).UseIdentityAlwaysColumn();
        builder.Property(a => a.Actor).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Action).HasMaxLength(64).IsRequired();
        builder.Property(a => a.Target).HasMaxLength(200);
        builder.Property(a => a.IpAddress).HasMaxLength(64);
        builder.Property(a => a.Details).HasColumnType("jsonb");
        builder.HasIndex(a => a.OccurredAt).IsDescending();
        builder.HasIndex(a => new { a.Actor, a.OccurredAt }).IsDescending(false, true);
    }
}
