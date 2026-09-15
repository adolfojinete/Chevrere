using YaaJuu.Modules.Identity.Domain;
using YaaJuu.Modules.Tenancy.Application.Contracts;
using YaaJuu.Modules.Tenancy.Application.Queries;
using YaaJuu.SharedKernel.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace YaaJuu.Api.Controllers;

[ApiController]
[Route("api/v1/admin/audit-events")]
[Authorize(Policy = AuthorizationPolicies.PlatformStaff)]
public sealed class AdminAuditEventsController(
    IHandler<ListAuditEventsQuery, IReadOnlyList<AuditEventDto>> handler) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AuditEventDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? tenantId,
        [FromQuery] Guid? franchiseeId,
        CancellationToken cancellationToken)
    {
        var events = await handler.HandleAsync(new ListAuditEventsQuery(tenantId, franchiseeId), cancellationToken);
        return Ok(events);
    }
}
