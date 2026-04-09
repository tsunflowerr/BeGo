using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OptiGo.Domain.Entities;

namespace OptiGo.Infrastructure.Persistence.Configurations;

public class VoteConfiguration : IEntityTypeConfiguration<Vote>
{
    public void Configure(EntityTypeBuilder<Vote> builder)
    {
        builder.ToTable("votes");

        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(v => v.SessionId)
            .HasColumnName("session_id")
            .IsRequired();

        builder.Property(v => v.MemberId)
            .HasColumnName("member_id")
            .IsRequired();

        builder.Property(v => v.VenueId)
            .HasColumnName("venue_id")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(v => v.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.HasIndex(v => new { v.SessionId, v.MemberId })
            .HasDatabaseName("idx_votes_session_member")
            .IsUnique();

        builder.HasOne(v => v.Member)
            .WithMany()
            .HasForeignKey(v => v.MemberId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
