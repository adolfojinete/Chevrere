using System.Diagnostics.CodeAnalysis;
using Chevrere.Api.Http;
using Chevrere.Modules.Identity.Domain;
using Chevrere.Modules.Procurement.Application.Contracts;
using Chevrere.Modules.Procurement.Application.Queries;
using Chevrere.SharedKernel.Application;
using Chevrere.SharedKernel.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Chevrere.Api.Controllers;

/// <summary>
/// Read-only procurement surface for Chevrere staff: supervision without mutating a tenant's supply chain.
/// </summary>
[ApiController]
[Route("api/v1/admin/stores/{storeId:guid}")]
[Authorize(Policy = AuthorizationPolicies.PlatformStaff)]
[SuppressMessage("csharpsquid", "S6960", Justification = "REST resource: read-only procurement views of one store stay together.")]
public sealed class AdminStoreProcurementController(
    IHandler<ListPurchaseOrdersQuery, Result<PagedResult<PurchaseOrderSummaryDto>>> listHandler,
    IHandler<GetPurchaseOrderQuery, Result<PurchaseOrderDto>> getHandler,
    IHandler<ListGoodsReceiptsQuery, Result<PagedResult<GoodsReceiptSummaryDto>>> receiptsHandler,
    IHandler<GetGoodsReceiptQuery, Result<GoodsReceiptDto>> receiptHandler) : ControllerBase
{
    [HttpGet("purchase-orders")]
    [ProducesResponseType(typeof(PagedResult<PurchaseOrderSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListPurchaseOrders(
        Guid storeId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => this.ToActionResult(await listHandler.HandleAsync(
            new ListPurchaseOrdersQuery(storeId, page, pageSize),
            cancellationToken));

    [HttpGet("purchase-orders/{id:guid}")]
    [ProducesResponseType(typeof(PurchaseOrderDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPurchaseOrder(Guid storeId, Guid id, CancellationToken cancellationToken)
        => this.ToActionResult(await getHandler.HandleAsync(new GetPurchaseOrderQuery(storeId, id), cancellationToken));

    [HttpGet("purchase-orders/{id:guid}/receipts")]
    [ProducesResponseType(typeof(PagedResult<GoodsReceiptSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListReceipts(
        Guid storeId,
        Guid id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => this.ToActionResult(await receiptsHandler.HandleAsync(
            new ListGoodsReceiptsQuery(storeId, id, page, pageSize),
            cancellationToken));

    [HttpGet("goods-receipts/{receiptId:guid}")]
    [ProducesResponseType(typeof(GoodsReceiptDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetReceipt(Guid storeId, Guid receiptId, CancellationToken cancellationToken)
        => this.ToActionResult(await receiptHandler.HandleAsync(
            new GetGoodsReceiptQuery(storeId, receiptId),
            cancellationToken));
}
