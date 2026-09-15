using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace YaaJuu.Infrastructure.Persistence.Configurations;

internal static class ConcurrencyExtensions
{
    public static void ConfigureXminConcurrency<T>(this EntityTypeBuilder<T> builder)
        where T : class
    {
        // xmin is a PostgreSQL system column. Do not emit it in CREATE TABLE.
        builder.Property<uint>("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();
    }
}
