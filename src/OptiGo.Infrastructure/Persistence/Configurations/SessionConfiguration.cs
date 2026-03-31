using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OptiGo.Domain.Entities;
using OptiGo.Domain.Enums;

namespace OptiGo.Infrastructure.Persistence.Configurations;

public class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    public void Configure(EntityTypeBuilder<Session> builder)
    {
        builder.ToTable("sessions");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasColumnName("id")
            .ValueGeneratedNever(); // Domain tự generate Guid

        builder.Property(s => s.HostName)
            .HasColumnName("host_name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(s => s.Status)
            .HasColumnName("status")
            .HasConversion<string>() // Lưu enum dạng string cho readability
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(s => s.QueryText)
            .HasColumnName("query_text")
            .HasColumnType("text");

        builder.Property(s => s.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(s => s.ExpiresAt)
            .HasColumnName("expires_at")
            .IsRequired();

        // Cấu hình relationship Session → Members (1:N)
        // EF Core access backing field "_members" 
        builder.HasMany(s => s.Members)
            .WithOne(m => m.Session)
            .HasForeignKey(m => m.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata
            .FindNavigation(nameof(Session.Members))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        // Cấu hình relationship Session → Votes (1:N)
        builder.HasMany(s => s.Votes)
            .WithOne(v => v.Session)
            .HasForeignKey(v => v.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata
            .FindNavigation(nameof(Session.Votes))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
