using YaaJuu.Modules.Orders.Application.Commands;
using YaaJuu.Modules.Orders.Application.Contracts;
using YaaJuu.Modules.Orders.Application.Queries;
using YaaJuu.SharedKernel.Application;
using YaaJuu.SharedKernel.Results;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace YaaJuu.Modules.Orders.Application;

public static class OrdersApplicationExtensions
{
    public static IServiceCollection AddOrdersApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(OrdersApplicationExtensions).Assembly, includeInternalTypes: true);

        services.AddScoped<IHandler<ViewCartQuery, Result<CartViewDto>>, ViewCartHandler>();
        services.AddScoped<IHandler<PutCartItemCommand, Result<CartViewDto>>, PutCartItemHandler>();
        services.AddScoped<IHandler<RemoveCartItemCommand, Result<CartViewDto>>, RemoveCartItemHandler>();
        services.AddScoped<IHandler<ClearCartCommand, Result<CartViewDto>>, ClearCartHandler>();

        services.AddScoped<IHandler<CreateOrderCommand, Result<ConsumerOrderDto>>, CreateOrderHandler>();
        services.AddScoped<IHandler<CancelOrderCommand, Result<ConsumerOrderDto>>, CancelOrderHandler>();
        services.AddScoped<IHandler<ExpireOrderCommand, Result>, ExpireOrderHandler>();

        services.AddScoped<IHandler<ListConsumerOrdersQuery, Result<PagedResult<ConsumerOrderSummaryDto>>>, ListConsumerOrdersHandler>();
        services.AddScoped<IHandler<GetConsumerOrderQuery, Result<ConsumerOrderDto>>, GetConsumerOrderHandler>();
        services.AddScoped<IHandler<ListBusinessOrdersQuery, Result<PagedResult<BusinessOrderSummaryDto>>>, ListBusinessOrdersHandler>();
        services.AddScoped<IHandler<GetBusinessOrderQuery, Result<BusinessOrderDto>>, GetBusinessOrderHandler>();
        services.AddScoped<IHandler<GetAdminOrderQuery, Result<AdminOrderDto>>, GetAdminOrderHandler>();

        return services;
    }
}
