using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OptiGo.Domain.Entities;

namespace OptiGo.Infrastructure.Persistence.Configurations;

public class PickupRequestConfiguration : IEntityTypeConfiguration<PickupRequest>
{
    public void Configure(EntityTypeBuilder<PickupRequest> builder)
    {
        builder.ToTable("pickup_requests");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(r => r.SessionId)
            .HasColumnName("session_id")
            .IsRequired();

        builder.Property(r => r.PassengerId)
            .HasColumnName("passenger_id")
            .IsRequired();

        builder.Property(r => r.AcceptedDriverId)
            .HasColumnName("accepted_driver_id");

        builder.Property(r => r.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(r => r.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(r => r.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.HasIndex(r => new { r.SessionId, r.PassengerId })
            .HasDatabaseName("idx_pickup_requests_session_passenger");

        builder.HasOne(r => r.Passenger)
            .WithMany()
            .HasForeignKey(r => r.PassengerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.AcceptedDriver)
            .WithMany()
            .HasForeignKey(r => r.AcceptedDriverId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
