using Chevrere.Modules.Procurement.Application;
using Chevrere.Modules.Procurement.Application.Abstractions;
using Chevrere.Modules.Procurement.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Chevrere.Modules.Procurement.Infrastructure;

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
