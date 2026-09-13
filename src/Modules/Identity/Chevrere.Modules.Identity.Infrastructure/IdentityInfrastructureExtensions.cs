using Chevrere.Modules.Identity.Application;
using Chevrere.Modules.Identity.Application.Abstractions;
using Chevrere.Modules.Identity.Domain;
using Chevrere.Modules.Identity.Infrastructure.Authentication;
using Chevrere.Modules.Identity.Infrastructure.Provisioning;
using Microsoft.Extensions.DependencyInjection;

namespace Chevrere.Modules.Identity.Infrastructure;

public static class IdentityInfrastructureExtensions
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services)
    {
        services.AddIdentityApplication();
        services.AddScoped<ITokenIssuer, TokenIssuer>();
        services.AddScoped<IUserAuthenticator, UserAuthenticator>();
        services.AddScoped<IIdentityProvisioning, IdentityProvisioning>();

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthorizationPolicies.PlatformStaff, policy =>
                policy.RequireRole(
                    RoleNames.PlatformSuperAdmin,
                    RoleNames.PlatformAdmin,
                    RoleNames.PlatformSupport))
            .AddPolicy(AuthorizationPolicies.PlatformOperators, policy =>
                policy.RequireRole(RoleNames.PlatformSuperAdmin, RoleNames.PlatformAdmin))
            .AddPolicy(AuthorizationPolicies.PlatformSuperAdmin, policy =>
                policy.RequireRole(RoleNames.PlatformSuperAdmin))
            .AddPolicy(AuthorizationPolicies.FranchiseeOwner, policy =>
                policy.RequireRole(RoleNames.FranchiseeOwner));

        return services;
    }
}
