using YaaJuu.Modules.Procurement.Application.Commands;
using YaaJuu.Modules.Procurement.Application.Contracts;
using YaaJuu.Modules.Procurement.Application.Queries;
using YaaJuu.SharedKernel.Application;
using YaaJuu.SharedKernel.Results;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace YaaJuu.Modules.Procurement.Application;

public static class ProcurementApplicationExtensions
{
    public static IServiceCollection AddProcurementApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(ProcurementApplicationExtensions).Assembly, includeInternalTypes: true);

        services.AddScoped<IHandler<CreateSupplierRequest, Result<SupplierDto>>, CreateSupplierHandler>();
        services.AddScoped<IHandler<UpdateSupplierCommand, Result<SupplierDto>>, UpdateSupplierHandler>();
        services.AddScoped<IHandler<ActivateSupplierCommand, Result>, ActivateSupplierHandler>();
        services.AddScoped<IHandler<DeactivateSupplierCommand, Result>, DeactivateSupplierHandler>();
        services.AddScoped<IHandler<ListSuppliersQuery, Result<PagedResult<SupplierDto>>>, ListSuppliersHandler>();
        services.AddScoped<IHandler<GetSupplierQuery, Result<SupplierDto>>, GetSupplierHandler>();

        services.AddScoped<IHandler<CreatePurchaseOrderCommand, Result<PurchaseOrderDto>>, CreatePurchaseOrderHandler>();
        services.AddScoped<IHandler<UpdatePurchaseOrderDraftCommand, Result<PurchaseOrderDto>>, UpdatePurchaseOrderDraftHandler>();
        services.AddScoped<IHandler<ApprovePurchaseOrderCommand, Result<PurchaseOrderDto>>, ApprovePurchaseOrderHandler>();
        services.AddScoped<IHandler<CancelPurchaseOrderCommand, Result<PurchaseOrderDto>>, CancelPurchaseOrderHandler>();
        services.AddScoped<IHandler<ListPurchaseOrdersQuery, Result<PagedResult<PurchaseOrderSummaryDto>>>, ListPurchaseOrdersHandler>();
        services.AddScoped<IHandler<GetPurchaseOrderQuery, Result<PurchaseOrderDto>>, GetPurchaseOrderHandler>();

        services.AddScoped<IHandler<ReceiveGoodsCommand, Result<GoodsReceiptDto>>, ReceiveGoodsHandler>();
        services.AddScoped<IHandler<ListGoodsReceiptsQuery, Result<PagedResult<GoodsReceiptSummaryDto>>>, ListGoodsReceiptsHandler>();
        services.AddScoped<IHandler<GetGoodsReceiptQuery, Result<GoodsReceiptDto>>, GetGoodsReceiptHandler>();

        return services;
    }
}
