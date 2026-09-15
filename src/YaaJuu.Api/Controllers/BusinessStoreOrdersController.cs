using System.Diagnostics.CodeAnalysis;
using YaaJuu.Api.Http;
using YaaJuu.Modules.Identity.Domain;
using YaaJuu.Modules.Orders.Application.Contracts;
using YaaJuu.Modules.Orders.Application.Queries;
using YaaJuu.SharedKernel.Application;
using YaaJuu.SharedKernel.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace YaaJuu.Api.Controllers;

[ApiController]
[Route("api/v1/business/stores/{storeId:guid}/orders")]
[Authorize(Policy = AuthorizationPolicies.FranchiseeOwner)]
[SuppressMessage("csharpsquid", "S6960", Justification = "REST resource: store orders stay together.")]
public sealed class BusinessStoreOrdersController(
    IHandler<ListBusinessOrdersQuery, Result<PagedResult<BusinessOrderSummaryDto>>> listHandler,
    IHandler<GetBusinessOrderQuery, Result<BusinessOrderDto>> getHandler) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<BusinessOrderSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        Guid storeId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        this.ToActionResult(await listHandler.HandleAsync(
            new ListBusinessOrdersQuery(storeId, page, pageSize), cancellationToken));

    [HttpGet("{orderId:guid}")]
    [ProducesResponseType(typeof(BusinessOrderDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid storeId, Guid orderId, CancellationToken cancellationToken) =>
        this.ToActionResult(await getHandler.HandleAsync(
            new GetBusinessOrderQuery(storeId, orderId), cancellationToken));
}
