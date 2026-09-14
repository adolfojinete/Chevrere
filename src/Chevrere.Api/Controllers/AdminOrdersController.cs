using Chevrere.Api.Http;
using Chevrere.Modules.Identity.Domain;
using Chevrere.Modules.Orders.Application.Contracts;
using Chevrere.Modules.Orders.Application.Queries;
using Chevrere.SharedKernel.Application;
using Chevrere.SharedKernel.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Chevrere.Api.Controllers;

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
