using Chevrere.Infrastructure.Identity;
using Chevrere.Infrastructure.Persistence;
using Chevrere.Modules.Identity.Application.Abstractions;
using Chevrere.Modules.Identity.Application.Contracts;
using Chevrere.Modules.Identity.Domain;
using Chevrere.SharedKernel.Results;
using Chevrere.SharedKernel.Time;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Chevrere.Modules.Identity.Infrastructure.Provisioning;

public sealed class IdentityProvisioning(
    ChevrereDbContext dbContext,
    IPasswordHasher<ApplicationUser> passwordHasher,
    IClock clock) : IIdentityProvisioning
{
    public async Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken)
    {
        var normalized = email.Trim().ToUpperInvariant();
        return await dbContext.Users.AnyAsync(u => u.NormalizedEmail == normalized, cancellationToken);
    }

    public async Task<Result<OwnerUserResult>> AddOwnerAsync(
        OwnerUserRequest request,
        CancellationToken cancellationToken)
    {
        var email = request.Email.Trim();
        var normalized = email.ToUpperInvariant();

        if (await dbContext.Users.AnyAsync(u => u.NormalizedEmail == normalized, cancellationToken))
        {
            return Result.Failure<OwnerUserResult>(
                Error.Conflict("owner.email.duplicate", "The owner email is already in use."));
        }

        var role = await dbContext.Roles.SingleOrDefaultAsync(
            r => r.NormalizedName == RoleNames.FranchiseeOwner.ToUpperInvariant(),
            cancellationToken);

        if (role is null)
        {
            return Result.Failure<OwnerUserResult>(
                Error.Domain("role.missing", "The FranchiseeOwner role has not been seeded."));
        }

        var now = clock.UtcNow;
        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            TenantId = request.TenantId,
            UserName = email,
            NormalizedUserName = normalized,
            Email = email.ToLowerInvariant(),
            NormalizedEmail = normalized,
            EmailConfirmed = true,
            DisplayName = request.DisplayName.Trim(),
            IsActive = true,
            SecurityStamp = Guid.CreateVersion7().ToString("N"),
            CreatedAt = now,
            UpdatedAt = now
        };

        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);

        dbContext.Users.Add(user);
        dbContext.UserRoles.Add(new IdentityUserRole<Guid>
        {
            UserId = user.Id,
            RoleId = role.Id
        });

        return Result.Success(new OwnerUserResult(user.Id, user.Email));
    }

    public async Task<Result<ConsumerUserResult>> AddConsumerAsync(
        string email,
        string password,
        string displayName,
        CancellationToken cancellationToken)
    {
        var trimmedEmail = GuardEmail(email);
        var normalized = trimmedEmail.ToUpperInvariant();

        if (await dbContext.Users.AnyAsync(u => u.NormalizedEmail == normalized, cancellationToken))
        {
            return Result.Failure<ConsumerUserResult>(
                Error.Conflict("consumer.email.duplicate", "The consumer email is already in use."));
        }

        var role = await dbContext.Roles.SingleOrDefaultAsync(
            r => r.NormalizedName == RoleNames.Consumer.ToUpperInvariant(),
            cancellationToken);

        if (role is null)
        {
            return Result.Failure<ConsumerUserResult>(
                Error.Domain("role.missing", "The Consumer role has not been seeded."));
        }

        var now = clock.UtcNow;
        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            TenantId = null,
            UserName = trimmedEmail,
            NormalizedUserName = normalized,
            Email = trimmedEmail.ToLowerInvariant(),
            NormalizedEmail = normalized,
            EmailConfirmed = true,
            DisplayName = displayName.Trim(),
            IsActive = true,
            SecurityStamp = Guid.CreateVersion7().ToString("N"),
            CreatedAt = now,
            UpdatedAt = now
        };

        user.PasswordHash = passwordHasher.HashPassword(user, password);

        dbContext.Users.Add(user);
        dbContext.UserRoles.Add(new IdentityUserRole<Guid>
        {
            UserId = user.Id,
            RoleId = role.Id
        });

        return Result.Success(new ConsumerUserResult(user.Id, user.Email));
    }

    private static string GuardEmail(string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        return email.Trim();
    }
}
