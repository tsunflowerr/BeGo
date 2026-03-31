using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OptiGo.Domain.Entities;

namespace OptiGo.Infrastructure.Persistence.Configurations;

public class VenueConfiguration : IEntityTypeConfiguration<Venue>
{
    public void Configure(EntityTypeBuilder<Venue> builder)
    {
        builder.ToTable("venues");

        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id)
            .HasColumnName("id")
            .HasMaxLength(255)
            .ValueGeneratedNever();

        builder.Property(v => v.Name)
            .HasColumnName("name")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(v => v.Category)
            .HasColumnName("category")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(v => v.Latitude)
            .HasColumnName("latitude")
            .HasColumnType("decimal(10,7)")
            .IsRequired();

        builder.Property(v => v.Longitude)
            .HasColumnName("longitude")
            .HasColumnType("decimal(10,7)")
            .IsRequired();

        builder.Property(v => v.Rating)
            .HasColumnName("rating")
            .HasColumnType("decimal(2,1)");

        builder.Property(v => v.ReviewCount)
            .HasColumnName("review_count");

        builder.Property(v => v.PriceLevel)
            .HasColumnName("price_level");

        builder.Property(v => v.Address)
            .HasColumnName("address")
            .HasColumnType("text");

        builder.Property(v => v.CachedAt)
            .HasColumnName("cached_at")
            .IsRequired();

        // Index: tìm venue theo category nhanh
        builder.HasIndex(v => v.Category)
            .HasDatabaseName("idx_venues_category");
    }
}
