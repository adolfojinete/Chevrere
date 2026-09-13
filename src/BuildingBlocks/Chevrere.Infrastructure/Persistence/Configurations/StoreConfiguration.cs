using Chevrere.Modules.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Chevrere.Infrastructure.Persistence.Configurations;

public sealed class StoreConfiguration : IEntityTypeConfiguration<Store>
{
    public void Configure(EntityTypeBuilder<Store> builder)
    {
        builder.ToTable("stores");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.AddressInternal).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Latitude).HasPrecision(9, 6);
        builder.Property(x => x.Longitude).HasPrecision(9, 6);

        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        builder.HasIndex(x => x.Status);
        builder.HasAlternateKey(x => new { x.Id, x.TenantId });

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Franchisee>()
            .WithMany()
            .HasForeignKey(x => new { x.FranchiseeId, x.TenantId })
            .HasPrincipalKey(x => new { x.Id, x.TenantId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
