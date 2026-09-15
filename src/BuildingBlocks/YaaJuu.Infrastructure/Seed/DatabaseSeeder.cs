using YaaJuu.Infrastructure.Identity;
using YaaJuu.Infrastructure.Options;
using YaaJuu.Infrastructure.Persistence;
using YaaJuu.Modules.Identity.Domain;
using YaaJuu.Modules.Subscriptions.Domain;
using YaaJuu.Modules.Subscriptions.Domain.ValueObjects;
using YaaJuu.SharedKernel.Context;
using YaaJuu.SharedKernel.Time;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace YaaJuu.Infrastructure.Seed;

public sealed class DatabaseSeeder(
    YaaJuuDbContext dbContext,
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
            EnsureSucceeded(result, $"Unable to seed role {roleName}");
        }

        _ = cancellationToken;
    }

    private async Task SeedPlanAsync(CancellationToken cancellationToken)
    {
        var options = planSeedOptions.Value;
        var code = options.Code.Trim().ToUpperInvariant();
        var existing = await dbContext.Plans.SingleOrDefaultAsync(p => p.Code == code, cancellationToken);
        if (existing is null)
        {
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
            return;
        }

        if (!string.Equals(existing.Name, options.Name.Trim(), StringComparison.Ordinal))
        {
            existing.Rename(options.Name, clock.UtcNow);
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Updated seeded plan {PlanCode} display name", code);
        }
    }

    private async Task SeedSuperAdminAsync(CancellationToken cancellationToken)
    {
        var options = bootstrapOptions.Value.SuperAdmin;
        var existing = await FindBootstrapSuperAdminAsync(options.Email);
        if (existing is not null)
        {
            await AlignBootstrapSuperAdminAsync(existing, options);
            return;
        }

        if (string.IsNullOrWhiteSpace(options.Password))
        {
            logger.LogWarning(
                "Bootstrap SuperAdmin password is not configured. Set Bootstrap__SuperAdmin__Password or User Secrets.");
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

        EnsureSucceeded(await userManager.CreateAsync(user, options.Password), "Unable to seed SuperAdmin");
        EnsureSucceeded(
            await userManager.AddToRoleAsync(user, RoleNames.PlatformSuperAdmin),
            "Unable to assign SuperAdmin role");

        logger.LogInformation("Seeded platform SuperAdmin {Email}", options.Email);
        _ = cancellationToken;
    }

    private async Task<ApplicationUser?> FindBootstrapSuperAdminAsync(string configuredEmail)
    {
        var byEmail = await userManager.FindByEmailAsync(configuredEmail);
        if (byEmail is not null)
        {
            return byEmail;
        }

        var superAdmins = await userManager.GetUsersInRoleAsync(RoleNames.PlatformSuperAdmin);
        return superAdmins
            .Where(user => user.TenantId is null)
            .OrderBy(user => user.CreatedAt)
            .ThenBy(user => user.Id)
            .FirstOrDefault();
    }

    private async Task AlignBootstrapSuperAdminAsync(ApplicationUser user, SuperAdminBootstrapOptions options)
    {
        var email = options.Email.Trim();
        var displayName = options.DisplayName.Trim();
        var changed = false;

        if (!string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
        {
            EnsureSucceeded(await userManager.SetEmailAsync(user, email), "Unable to update SuperAdmin email");
            changed = true;
        }

        if (!string.Equals(user.UserName, email, StringComparison.OrdinalIgnoreCase))
        {
            EnsureSucceeded(await userManager.SetUserNameAsync(user, email), "Unable to update SuperAdmin user name");
            changed = true;
        }

        if (!string.Equals(user.DisplayName, displayName, StringComparison.Ordinal))
        {
            user.DisplayName = displayName;
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        user.UpdatedAt = clock.UtcNow;
        EnsureSucceeded(await userManager.UpdateAsync(user), "Unable to update SuperAdmin");
        logger.LogInformation("Aligned bootstrap SuperAdmin {UserId} to {Email}", user.Id, email);
    }

    private static void EnsureSucceeded(IdentityResult result, string action)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"{action}: {string.Join(", ", result.Errors.Select(error => error.Description))}");
        }
    }
}
