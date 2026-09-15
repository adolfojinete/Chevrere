using YaaJuu.Infrastructure.Persistence;
using YaaJuu.SharedKernel.Payments;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;

namespace YaaJuu.IntegrationTests;

public sealed class YaaJuuApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Must match deploy/docker-compose.yml: the AddConsumerDiscovery migration creates the postgis
    // extension, which plain postgres:17-alpine does not ship.
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgis/postgis:17-3.5-alpine")
        .WithDatabase("yaajuu_test")
        .WithUsername("yaajuu")
        .WithPassword("yaajuu_test_only")
        .Build();

    public FakePaymentProvider FakePayments { get; } = new();

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
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPaymentProvider>();
            services.AddSingleton(FakePayments);
            services.AddSingleton<IPaymentProvider>(sp => sp.GetRequiredService<FakePaymentProvider>());
        });
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
        _ = scope.ServiceProvider.GetRequiredService<YaaJuuDbContext>();
    }

    private Dictionary<string, string?> TestSettings() => new()
    {
        ["ConnectionStrings:Database"] = _postgres.GetConnectionString(),
        ["Jwt:Issuer"] = "yaajuu",
        ["Jwt:Audience"] = "yaajuu-api",
        ["Jwt:SigningKey"] = "INTEGRATION_TEST_SIGNING_KEY_32CH!!",
        ["Jwt:AccessTokenMinutes"] = "60",
        ["Bootstrap:SuperAdmin:Email"] = "admin@yaajuu.test",
        ["Bootstrap:SuperAdmin:Password"] = "AdminTest!23456",
        ["Bootstrap:SuperAdmin:DisplayName"] = "Test Super Admin",
        ["PlanSeed:Code"] = "STANDARD",
        ["PlanSeed:Name"] = "YaaJuu Standard",
        ["PlanSeed:MonthlyPrice"] = "350000",
        ["PlanSeed:Currency"] = "COP",
        ["Orders:ReservationTtlMinutes"] = "15",
        ["Orders:ExpirationPollSeconds"] = "60",
        ["Orders:ExpirationBatchSize"] = "100",
        ["Orders:ExpirationWorkerEnabled"] = "false",
        ["Payments:Enabled"] = "true",
        ["Payments:SecretsMasterKey"] = Convert.ToBase64String(
            System.Security.Cryptography.SHA256.HashData("yaajuu-integration-payment-secrets-v1"u8.ToArray())),
        ["Payments:Wompi:Environment"] = "Sandbox",
        ["Payments:Wompi:TimeoutSeconds"] = "5",
        ["Payments:Reconciliation:Enabled"] = "false",
        ["Payments:Reconciliation:PollIntervalSeconds"] = "30",
        ["Payments:Reconciliation:BatchSize"] = "50",
        ["Payments:Reconciliation:MinimumAttemptAgeSeconds"] = "0"
    };
}
