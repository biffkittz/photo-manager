using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoManager.Data.Entities;

namespace PhotoManager.Data.Configurations;

/// <summary>Table and indexes for <see cref="FamilyMember"/>.</summary>
internal sealed class FamilyMemberConfiguration : IEntityTypeConfiguration<FamilyMember>
{
    public void Configure(EntityTypeBuilder<FamilyMember> builder)
    {
        builder.ToTable("family_members");
        builder.HasKey(m => m.Id);

        // Negative ids (-1, -2, ...): GitHub ids are positive, so a user id identifies its kind of account.
        builder.Property(m => m.Id)
            .UseIdentityByDefaultColumn()
            .HasIdentityOptions(startValue: -1, incrementBy: -1, minValue: long.MinValue, maxValue: -1);

        builder.Property(m => m.DisplayName).HasMaxLength(100).IsRequired();
        builder.Property(m => m.GoogleSubject).HasMaxLength(255);
        builder.Property(m => m.GoogleEmail).HasMaxLength(320);
        builder.Property(m => m.InviteEmail).HasMaxLength(320);
        builder.Property(m => m.InviteTokenHash).HasMaxLength(64).IsFixedLength();
        builder.Property(m => m.InvitedBy).HasMaxLength(100).IsRequired();
        builder.Property(m => m.Version).IsRowVersion();

        // One current member per Google account (removed members keep theirs for the record), and every pending
        // invite token is unique.
        builder.HasIndex(m => m.GoogleSubject).IsUnique().HasFilter("google_subject IS NOT NULL AND removed_at IS NULL");
        builder.HasIndex(m => m.InviteTokenHash).IsUnique().HasFilter("invite_token_hash IS NOT NULL");
    }
}
