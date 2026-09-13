using System.Diagnostics.CodeAnalysis;
using Chevrere.SharedKernel.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Chevrere.Infrastructure.Persistence;

[ExcludeFromCodeCoverage]
public sealed class ChevrereDbContextFactory : IDesignTimeDbContextFactory<ChevrereDbContext>
{
    public ChevrereDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(Directory.GetCurrentDirectory()))
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("Database")
            ?? "Host=localhost;Port=5432;Database=chevrere;Username=chevrere;Password=chevrere_dev_only";

        var options = new DbContextOptionsBuilder<ChevrereDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new ChevrereDbContext(options, new DesignTimeCurrentUser(), new DesignTimeTenantBypass());
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
