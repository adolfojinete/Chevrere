using YaaJuu.Modules.Tenancy.Application;
using YaaJuu.Modules.Tenancy.Application.Abstractions;
using YaaJuu.Modules.Tenancy.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace YaaJuu.Modules.Tenancy.Infrastructure;

public static class TenancyInfrastructureExtensions
{
    public static IServiceCollection AddTenancyModule(this IServiceCollection services)
    {
        services.AddTenancyApplication();
        services.AddScoped<ITenancyStore, TenancyStore>();
        return services;
    }
}
