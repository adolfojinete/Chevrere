using YaaJuu.Modules.Procurement.Application;
using YaaJuu.Modules.Procurement.Application.Abstractions;
using YaaJuu.Modules.Procurement.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace YaaJuu.Modules.Procurement.Infrastructure;

public static class ProcurementInfrastructureExtensions
{
    public static IServiceCollection AddProcurementModule(this IServiceCollection services)
    {
        services.AddProcurementApplication();
        services.AddScoped<IProcurementStore, ProcurementStore>();
        services.AddScoped<IProcurementStoreAccess, ProcurementStoreAccess>();
        services.AddScoped<IDocumentNumberGenerator, DocumentNumberGenerator>();
        return services;
    }
}
