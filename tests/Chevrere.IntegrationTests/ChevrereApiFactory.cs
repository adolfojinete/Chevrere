using Chevrere.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;

namespace Chevrere.IntegrationTests;

public sealed class ChevrereApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("chevrere_test")
        .WithUsername("chevrere")
        .WithPassword("chevrere_test_only")
        .Build();

    public async Task InitializeAsync() => await _postgres.StartAsync();

    public new async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(TestSettings()));
        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        foreach (var (key, value) in TestSettings())
        {
            if (value is not null)
            {
                builder.UseSetting(key, value);
            }
        }

        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(TestSettings()));
    }

    public HttpClient CreateClientUnredirected()
    {
        return CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    public async Task ResetTenantFilterAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        _ = scope.ServiceProvider.GetRequiredService<ChevrereDbContext>();
    }

    private Dictionary<string, string?> TestSettings() => new()
    {
        ["ConnectionStrings:Database"] = _postgres.GetConnectionString(),
        ["Jwt:Issuer"] = "chevrere",
        ["Jwt:Audience"] = "chevrere-api",
        ["Jwt:SigningKey"] = "INTEGRATION_TEST_SIGNING_KEY_32CH!!",
        ["Jwt:AccessTokenMinutes"] = "60",
        ["Bootstrap:SuperAdmin:Email"] = "admin@chevrere.test",
        ["Bootstrap:SuperAdmin:Password"] = "AdminTest!23456",
        ["Bootstrap:SuperAdmin:DisplayName"] = "Test Super Admin",
        ["PlanSeed:Code"] = "STANDARD",
        ["PlanSeed:Name"] = "Chevrere Standard",
        ["PlanSeed:MonthlyPrice"] = "350000",
        ["PlanSeed:Currency"] = "COP"
    };
}
