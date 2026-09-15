using YaaJuu.Api.Http;
using YaaJuu.Modules.Identity.Domain;
using YaaJuu.Modules.Inventory.Application.Contracts;
using YaaJuu.Modules.Inventory.Application.Queries;
using YaaJuu.SharedKernel.Application;
using YaaJuu.SharedKernel.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace YaaJuu.Api.Controllers;

[ApiController]
[Route("api/v1/admin/stores/{storeId:guid}/inventory")]
[Authorize(Policy = AuthorizationPolicies.PlatformStaff)]
public sealed class AdminStoreInventoryController(
    IHandler<ListStoreInventoryQuery, Result<PagedResult<InventoryItemDto>>> listHandler) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<InventoryItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        Guid storeId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
        => this.ToActionResult(await listHandler.HandleAsync(
            new ListStoreInventoryQuery(storeId, page, pageSize, search),
            cancellationToken));
}
