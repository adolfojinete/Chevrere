using Chevrere.Infrastructure.Identity;
using Chevrere.Infrastructure.Options;
using Chevrere.Infrastructure.Persistence;
using Chevrere.Modules.Identity.Domain;
using Chevrere.Modules.Subscriptions.Domain;
using Chevrere.Modules.Subscriptions.Domain.ValueObjects;
using Chevrere.SharedKernel.Context;
using Chevrere.SharedKernel.Time;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Chevrere.Infrastructure.Seed;

public sealed class DatabaseSeeder(
    ChevrereDbContext dbContext,
    RoleManager<ApplicationRole> roleManager,
    UserManager<ApplicationUser> userManager,
    ITenantFilterBypass tenantFilterBypass,
    IOptions<BootstrapOptions> bootstrapOptions,
    IOptions<PlanSeedOptions> planSeedOptions,
    IClock clock,
    ILogger<DatabaseSeeder> logger)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        tenantFilterBypass.Enabled = true;
        try
        {
            await SeedRolesAsync(cancellationToken);
            await SeedPlanAsync(cancellationToken);
            await SeedSuperAdminAsync(cancellationToken);
        }
        finally
        {
            tenantFilterBypass.Enabled = false;
        }
    }

    private async Task SeedRolesAsync(CancellationToken cancellationToken)
    {
        foreach (var roleName in RoleNames.All)
        {
            if (await roleManager.RoleExistsAsync(roleName))
            {
                continue;
            }

            var result = await roleManager.CreateAsync(new ApplicationRole(roleName));
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Unable to seed role {roleName}: {string.Join(", ", result.Errors.Select(e => e.Description))}");
            }
        }

        _ = cancellationToken;
    }

    private async Task SeedPlanAsync(CancellationToken cancellationToken)
    {
        var options = planSeedOptions.Value;
        var code = options.Code.Trim().ToUpperInvariant();
        if (await dbContext.Plans.AnyAsync(p => p.Code == code, cancellationToken))
        {
            return;
        }

        var plan = Plan.Create(
            code,
            options.Name,
            options.Description,
            Money.Create(options.MonthlyPrice, options.Currency),
            BillingPeriod.Monthly,
            clock.UtcNow);

        dbContext.Plans.Add(plan);
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded plan {PlanCode}", code);
    }

    private async Task SeedSuperAdminAsync(CancellationToken cancellationToken)
    {
        var options = bootstrapOptions.Value.SuperAdmin;
        if (string.IsNullOrWhiteSpace(options.Password))
        {
            logger.LogWarning(
                "Bootstrap SuperAdmin password is not configured. Set Bootstrap__SuperAdmin__Password or User Secrets.");
            return;
        }

        var existing = await userManager.FindByEmailAsync(options.Email);
        if (existing is not null)
        {
            return;
        }

        var now = clock.UtcNow;
        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = options.Email,
            Email = options.Email,
            NormalizedUserName = options.Email.ToUpperInvariant(),
            NormalizedEmail = options.Email.ToUpperInvariant(),
            EmailConfirmed = true,
            DisplayName = options.DisplayName,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };

        var create = await userManager.CreateAsync(user, options.Password);
        if (!create.Succeeded)
        {
            throw new InvalidOperationException(
                $"Unable to seed SuperAdmin: {string.Join(", ", create.Errors.Select(e => e.Description))}");
        }

        var role = await userManager.AddToRoleAsync(user, RoleNames.PlatformSuperAdmin);
        if (!role.Succeeded)
        {
            throw new InvalidOperationException(
                $"Unable to assign SuperAdmin role: {string.Join(", ", role.Errors.Select(e => e.Description))}");
        }

        logger.LogInformation("Seeded platform SuperAdmin {Email}", options.Email);
        _ = cancellationToken;
    }
}
