using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OptiGo.Domain.Entities;
using OptiGo.Domain.Enums;

namespace OptiGo.Infrastructure.Persistence.Configurations;

public class MemberConfiguration : IEntityTypeConfiguration<Member>
{
    public void Configure(EntityTypeBuilder<Member> builder)
    {
        builder.ToTable("members");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(m => m.SessionId)
            .HasColumnName("session_id")
            .IsRequired();

        builder.Property(m => m.Name)
            .HasColumnName("name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(m => m.Latitude)
            .HasColumnName("latitude")
            .HasColumnType("decimal(10,7)")
            .IsRequired();

        builder.Property(m => m.Longitude)
            .HasColumnName("longitude")
            .HasColumnType("decimal(10,7)")
            .IsRequired();

        builder.Property(m => m.AvatarUrl)
            .HasColumnName("avatar_url")
            .HasMaxLength(2048);

        builder.Property(m => m.AuthSubject)
            .HasColumnName("auth_subject")
            .HasMaxLength(255);

        builder.Property(m => m.AuthEmail)
            .HasColumnName("auth_email")
            .HasMaxLength(320);

        builder.Property(m => m.TransportMode)
            .HasColumnName("transport_mode")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(m => m.MobilityRole)
            .HasColumnName("mobility_role")
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(MemberMobilityRole.SelfTravel)
            .IsRequired();

        builder.Property(m => m.JoinedAt)
            .HasColumnName("joined_at")
            .IsRequired();

        builder.Property(m => m.DriverId)
            .HasColumnName("driver_id");

        builder.HasIndex(m => m.SessionId)
            .HasDatabaseName("idx_members_session");

        builder.HasIndex(m => new { m.SessionId, m.AuthSubject })
            .HasDatabaseName("idx_members_session_auth_subject");
    }
}
