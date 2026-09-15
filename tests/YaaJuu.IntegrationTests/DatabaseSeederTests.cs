using YaaJuu.Infrastructure;
using YaaJuu.Infrastructure.Identity;
using YaaJuu.Infrastructure.Persistence;
using YaaJuu.Infrastructure.Seed;
using YaaJuu.Modules.Identity.Domain;
using YaaJuu.Modules.Subscriptions.Domain;
using YaaJuu.Modules.Subscriptions.Domain.ValueObjects;
using YaaJuu.SharedKernel.Context;
using YaaJuu.SharedKernel.Time;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;

namespace YaaJuu.IntegrationTests;

public sealed class DatabaseSeederFixture : IAsyncLifetime
{
    public const string SuperAdminEmail = "admin@yaajuu.local";
    public const string SuperAdminPassword = "AdminTest!23456";
    public const string SuperAdminDisplayName = "Platform Super Admin";
    public const string PlanCode = "STANDARD";
    public const string PlanName = "YaaJuu Standard";
    public const string PreRenameAdminEmail = "admin@chevrere.local";
    public const string PreRenamePlanName = "Chevrere Standard";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgis/postgis:17-3.5-alpine")
        .WithDatabase("yaajuu_seeder_test")
        .WithUsername("yaajuu")
        .WithPassword("yaajuu_seeder_test_only")
        .Build();

    private ServiceProvider _services = null!;

    public IServiceProvider Services => _services;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = _postgres.GetConnectionString(),
                ["Jwt:Issuer"] = "yaajuu",
                ["Jwt:Audience"] = "yaajuu-api",
                ["Jwt:SigningKey"] = "INTEGRATION_TEST_SIGNING_KEY_32CH!!",
                ["Jwt:AccessTokenMinutes"] = "60",
                ["Bootstrap:SuperAdmin:Email"] = SuperAdminEmail,
                ["Bootstrap:SuperAdmin:Password"] = SuperAdminPassword,
                ["Bootstrap:SuperAdmin:DisplayName"] = SuperAdminDisplayName,
                ["PlanSeed:Code"] = PlanCode,
                ["PlanSeed:Name"] = PlanName,
                ["PlanSeed:Description"] = "Plan inicial de hipótesis comercial. El precio es configurable.",
                ["PlanSeed:MonthlyPrice"] = "350000",
                ["PlanSeed:Currency"] = "COP"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddYaaJuuInfrastructure(configuration);
        _services = services.BuildServiceProvider();

        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<YaaJuuDbContext>();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_services is not null)
        {
            await _services.DisposeAsync();
        }

        await _postgres.DisposeAsync();
    }
}

[Collection(Name)]
public sealed class DatabaseSeederTests(DatabaseSeederFixture fixture)
{
    public const string Name = "seeder";

    [Fact]
    public async Task Existing_pre_rename_standard_plan_is_not_duplicated_after_YaaJuu_seed()
    {
        var planted = await PlantPreRenameBootstrapAsync();

        await SeedYaaJuuAsync();

        await using var scope = fixture.Services.CreateAsyncScope();
        EnableBypass(scope);
        var snapshot = await SnapshotAsync(scope);

        Assert.Equal(1, snapshot.StandardPlanCount);
        Assert.Equal(1, snapshot.PlanCount);
        Assert.Equal(planted.PlanId, snapshot.StandardPlanId);
        Assert.Equal(DatabaseSeederFixture.PlanName, snapshot.StandardPlanName);
        Assert.Equal(1, snapshot.SuperAdminCount);
        Assert.Equal(planted.SuperAdminId, snapshot.SuperAdminId);
        Assert.Equal(planted.SubscriptionCount, snapshot.SubscriptionCount);
    }

    [Fact]
    public async Task Existing_pre_rename_bootstrap_admin_is_not_duplicated_after_YaaJuu_seed()
    {
        var planted = await PlantPreRenameBootstrapAsync();

        await SeedYaaJuuAsync();

        await using var scope = fixture.Services.CreateAsyncScope();
        EnableBypass(scope);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var snapshot = await SnapshotAsync(scope);

        Assert.Equal(1, snapshot.SuperAdminCount);
        Assert.Equal(planted.SuperAdminId, snapshot.SuperAdminId);
        Assert.Equal(DatabaseSeederFixture.SuperAdminEmail, snapshot.SuperAdminEmail);
        Assert.Equal(DatabaseSeederFixture.SuperAdminEmail, snapshot.SuperAdminUserName);
        Assert.Equal(DatabaseSeederFixture.SuperAdminDisplayName, snapshot.SuperAdminDisplayName);
        Assert.Null(await users.FindByEmailAsync(DatabaseSeederFixture.PreRenameAdminEmail));
        Assert.Equal(planted.PlanId, snapshot.StandardPlanId);
        Assert.Equal(1, snapshot.StandardPlanCount);
        Assert.Equal(planted.SubscriptionCount, snapshot.SubscriptionCount);

        var login = await users.CheckPasswordAsync(
            (await users.FindByIdAsync(planted.SuperAdminId.ToString()))!,
            DatabaseSeederFixture.SuperAdminPassword);
        Assert.True(login);
    }

    [Fact]
    public async Task YaaJuu_seeder_is_idempotent_when_run_multiple_times()
    {
        await SeedYaaJuuAsync();
        await using var firstScope = fixture.Services.CreateAsyncScope();
        EnableBypass(firstScope);
        var first = await SnapshotAsync(firstScope);

        for (var i = 0; i < 9; i++)
        {
            await SeedYaaJuuAsync();
        }

        await using var lastScope = fixture.Services.CreateAsyncScope();
        EnableBypass(lastScope);
        var last = await SnapshotAsync(lastScope);

        Assert.Equal(first, last);
        Assert.Equal(1, last.SuperAdminCount);
        Assert.Equal(1, last.StandardPlanCount);
        Assert.Equal(1, last.PlanCount);
        Assert.Equal(RoleNames.All.Count, last.RoleCount);
    }

    private async Task SeedYaaJuuAsync()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
        await seeder.SeedAsync();
    }

    private async Task<PlantedBootstrap> PlantPreRenameBootstrapAsync()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        EnableBypass(scope);
        var db = scope.ServiceProvider.GetRequiredService<YaaJuuDbContext>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        foreach (var roleName in RoleNames.All)
        {
            if (!await roles.RoleExistsAsync(roleName))
            {
                var created = await roles.CreateAsync(new ApplicationRole(roleName));
                Assert.True(created.Succeeded, string.Join(", ", created.Errors.Select(error => error.Description)));
            }
        }

        var plan = await db.Plans.SingleOrDefaultAsync(p => p.Code == DatabaseSeederFixture.PlanCode);
        if (plan is null)
        {
            plan = Plan.Create(
                DatabaseSeederFixture.PlanCode,
                DatabaseSeederFixture.PreRenamePlanName,
                "Plan inicial de hipótesis comercial. El precio es configurable.",
                Money.Create(350_000m, "COP"),
                BillingPeriod.Monthly,
                clock.UtcNow);
            db.Plans.Add(plan);
        }
        else
        {
            plan.Rename(DatabaseSeederFixture.PreRenamePlanName, clock.UtcNow);
        }

        await db.SaveChangesAsync();

        var superAdmins = (await users.GetUsersInRoleAsync(RoleNames.PlatformSuperAdmin))
            .Where(user => user.TenantId is null)
            .OrderBy(user => user.CreatedAt)
            .ThenBy(user => user.Id)
            .ToList();

        ApplicationUser admin;
        if (superAdmins.Count == 0)
        {
            var now = clock.UtcNow;
            admin = new ApplicationUser
            {
                Id = Guid.CreateVersion7(),
                UserName = DatabaseSeederFixture.PreRenameAdminEmail,
                Email = DatabaseSeederFixture.PreRenameAdminEmail,
                NormalizedUserName = DatabaseSeederFixture.PreRenameAdminEmail.ToUpperInvariant(),
                NormalizedEmail = DatabaseSeederFixture.PreRenameAdminEmail.ToUpperInvariant(),
                EmailConfirmed = true,
                DisplayName = "Chevrere Super Admin",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            };

            var create = await users.CreateAsync(admin, DatabaseSeederFixture.SuperAdminPassword);
            Assert.True(create.Succeeded, string.Join(", ", create.Errors.Select(error => error.Description)));
            var role = await users.AddToRoleAsync(admin, RoleNames.PlatformSuperAdmin);
            Assert.True(role.Succeeded, string.Join(", ", role.Errors.Select(error => error.Description)));
        }
        else
        {
            admin = superAdmins[0];
            var email = await users.SetEmailAsync(admin, DatabaseSeederFixture.PreRenameAdminEmail);
            Assert.True(email.Succeeded, string.Join(", ", email.Errors.Select(error => error.Description)));
            var userName = await users.SetUserNameAsync(admin, DatabaseSeederFixture.PreRenameAdminEmail);
            Assert.True(userName.Succeeded, string.Join(", ", userName.Errors.Select(error => error.Description)));
            admin.DisplayName = "Chevrere Super Admin";
            admin.UpdatedAt = clock.UtcNow;
            var update = await users.UpdateAsync(admin);
            Assert.True(update.Succeeded, string.Join(", ", update.Errors.Select(error => error.Description)));
        }

        return new PlantedBootstrap(
            plan.Id,
            admin.Id,
            await db.Subscriptions.CountAsync());
    }

    private static async Task<SeederSnapshot> SnapshotAsync(AsyncServiceScope scope)
    {
        var db = scope.ServiceProvider.GetRequiredService<YaaJuuDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();

        var plans = await db.Plans.OrderBy(plan => plan.Id).ToListAsync();
        var standard = Assert.Single(plans, plan => plan.Code == DatabaseSeederFixture.PlanCode);
        var superAdmins = (await users.GetUsersInRoleAsync(RoleNames.PlatformSuperAdmin))
            .Where(user => user.TenantId is null)
            .OrderBy(user => user.Id)
            .ToList();
        var superAdmin = Assert.Single(superAdmins);
        var roleIds = await roles.Roles.OrderBy(role => role.Id).Select(role => role.Id).ToListAsync();

        return new SeederSnapshot(
            PlanCount: plans.Count,
            StandardPlanCount: plans.Count(plan => plan.Code == DatabaseSeederFixture.PlanCode),
            StandardPlanId: standard.Id,
            StandardPlanName: standard.Name,
            SuperAdminCount: superAdmins.Count,
            SuperAdminId: superAdmin.Id,
            SuperAdminEmail: superAdmin.Email,
            SuperAdminUserName: superAdmin.UserName,
            SuperAdminDisplayName: superAdmin.DisplayName,
            RoleCount: roleIds.Count,
            RoleIds: string.Join(",", roleIds),
            SubscriptionCount: await db.Subscriptions.CountAsync());
    }

    private static void EnableBypass(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;

    private sealed record PlantedBootstrap(Guid PlanId, Guid SuperAdminId, int SubscriptionCount);

    private sealed record SeederSnapshot(
        int PlanCount,
        int StandardPlanCount,
        Guid StandardPlanId,
        string StandardPlanName,
        int SuperAdminCount,
        Guid SuperAdminId,
        string? SuperAdminEmail,
        string? SuperAdminUserName,
        string SuperAdminDisplayName,
        int RoleCount,
        string RoleIds,
        int SubscriptionCount);
}

[CollectionDefinition(DatabaseSeederTests.Name)]
public sealed class SeederCollection : ICollectionFixture<DatabaseSeederFixture>
{
}
