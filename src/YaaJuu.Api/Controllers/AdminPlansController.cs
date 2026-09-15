using YaaJuu.Modules.Identity.Domain;
using YaaJuu.Modules.Subscriptions.Application.Contracts;
using YaaJuu.Modules.Subscriptions.Application.Queries;
using YaaJuu.SharedKernel.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace YaaJuu.Api.Controllers;

[ApiController]
[Route("api/v1/admin/plans")]
[Authorize(Policy = AuthorizationPolicies.PlatformStaff)]
public sealed class AdminPlansController(IHandler<ListPlansQuery, IReadOnlyList<PlanDto>> handler) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<PlanDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var plans = await handler.HandleAsync(new ListPlansQuery(), cancellationToken);
        return Ok(plans);
    }
}
