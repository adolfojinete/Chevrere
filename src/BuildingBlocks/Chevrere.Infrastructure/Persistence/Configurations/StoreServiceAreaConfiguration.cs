using Chevrere.Modules.Consumer.Domain;
using Chevrere.Modules.Consumer.Domain.ValueObjects;
using Chevrere.Modules.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NetTopologySuite.Geometries;

namespace Chevrere.Infrastructure.Persistence.Configurations;

public sealed class StoreServiceAreaConfiguration : IEntityTypeConfiguration<StoreServiceArea>
{
    /// <summary>
    /// Name of the shadow PostGIS column. The domain only knows latitude and longitude; the geography
    /// point is a persistence concern kept in sync by <c>StoreServiceAreaStore</c>.
    /// </summary>
    public const string LocationProperty = "Location";

    public void Configure(EntityTypeBuilder<StoreServiceArea> builder)
    {
        builder.ToTable("store_service_areas", table =>
        {
            table.HasCheckConstraint(
                "ck_store_service_areas_radius_range",
                $"service_radius_meters BETWEEN {ServiceRadius.MinMeters} AND {ServiceRadius.MaxMeters}");
        });

        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.Id, x.TenantId });

        builder.Property(x => x.Latitude).IsRequired();
        builder.Property(x => x.Longitude).IsRequired();
        builder.Property(x => x.ServiceRadiusMeters).IsRequired();
        builder.Property(x => x.IsEnabled).IsRequired();

        builder.Property<Point>(LocationProperty)
            .HasColumnName("location")
            .HasColumnType("geography (Point, 4326)")
            .IsRequired();

        // One area per store. Two overlapping definitions would make "which store serves here"
        // depend on row order.
        builder.HasIndex(x => x.StoreId)
            .IsUnique()
            .HasDatabaseName("ix_store_service_areas_store_id");

        builder.HasIndex(x => x.TenantId)
            .HasDatabaseName("ix_store_service_areas_tenant_id");

        builder.HasIndex(LocationProperty)
            .HasMethod("gist")
            .HasDatabaseName("ix_store_service_areas_location");

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Store>()
            .WithMany()
            .HasForeignKey(x => new { x.StoreId, x.TenantId })
            .HasPrincipalKey(x => new { x.Id, x.TenantId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.ConfigureXminConcurrency();
    }
}
