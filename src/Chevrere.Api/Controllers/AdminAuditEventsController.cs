using Chevrere.Modules.Identity.Domain;
using Chevrere.Modules.Tenancy.Application.Contracts;
using Chevrere.Modules.Tenancy.Application.Queries;
using Chevrere.SharedKernel.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Chevrere.Api.Controllers;

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
