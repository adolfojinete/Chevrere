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
[Route("api/v1/admin/orders")]
[Authorize(Policy = AuthorizationPolicies.PlatformStaff)]
public sealed class AdminOrdersController(IHandler<GetAdminOrderQuery, Result<AdminOrderDto>> getHandler) : ControllerBase
{
    [HttpGet("{orderId:guid}")]
    [ProducesResponseType(typeof(AdminOrderDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid orderId, CancellationToken cancellationToken) =>
        this.ToActionResult(await getHandler.HandleAsync(new GetAdminOrderQuery(orderId), cancellationToken));
}
