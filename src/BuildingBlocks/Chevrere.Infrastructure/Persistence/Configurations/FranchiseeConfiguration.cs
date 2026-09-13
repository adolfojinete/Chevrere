using Chevrere.Modules.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Chevrere.Infrastructure.Persistence.Configurations;

public sealed class FranchiseeConfiguration : IEntityTypeConfiguration<Franchisee>
{
    public void Configure(EntityTypeBuilder<Franchisee> builder)
    {
        builder.ToTable("franchisees");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.LegalName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.TradeName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.IdentificationType).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.IdentificationNumber).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Email).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Phone).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => x.IdentificationNumber).IsUnique();
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.TradeName, x.LegalName });
        builder.HasAlternateKey(x => new { x.Id, x.TenantId });

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ConfigureXminConcurrency();
    }
}
