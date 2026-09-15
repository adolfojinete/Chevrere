using System.Diagnostics.CodeAnalysis;
using YaaJuu.SharedKernel.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace YaaJuu.Infrastructure.Persistence;

[ExcludeFromCodeCoverage]
public sealed class YaaJuuDbContextFactory : IDesignTimeDbContextFactory<YaaJuuDbContext>
{
    public YaaJuuDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(Directory.GetCurrentDirectory()))
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("Database")
            ?? "Host=localhost;Port=5432;Database=chevrere;Username=chevrere;Password=chevrere_dev_only";

        var options = new DbContextOptionsBuilder<YaaJuuDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite())
            .UseSnakeCaseNamingConvention()
            .Options;

        return new YaaJuuDbContext(options, new DesignTimeCurrentUser(), new DesignTimeTenantBypass());
    }

    private sealed class DesignTimeCurrentUser : ICurrentUser
    {
        public bool IsAuthenticated => false;

        public Guid? UserId => null;

        public Guid? TenantId => null;

        public string? Email => null;

        public bool IsPlatformUser => false;

        public IReadOnlyCollection<string> Roles => [];
    }

    private sealed class DesignTimeTenantBypass : ITenantFilterBypass
    {
        public bool Enabled { get; set; } = true;
    }
}
