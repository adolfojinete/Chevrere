using Chevrere.Modules.Tenancy.Application;
using Chevrere.Modules.Tenancy.Application.Abstractions;
using Chevrere.Modules.Tenancy.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Chevrere.Modules.Tenancy.Infrastructure;

public static class TenancyInfrastructureExtensions
{
    public static IServiceCollection AddTenancyModule(this IServiceCollection services)
    {
        services.AddTenancyApplication();
        services.AddScoped<ITenancyStore, TenancyStore>();
        return services;
    }
}
