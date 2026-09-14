using Chevrere.Api.Http;
using Chevrere.Modules.Consumer.Application.Contracts;
using Chevrere.Modules.Consumer.Application.Queries;
using Chevrere.Modules.Identity.Domain;
using Chevrere.SharedKernel.Application;
using Chevrere.SharedKernel.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Chevrere.Api.Controllers;

/// <summary>
/// An owner may read the area of their own store but not redraw it: coverage is a network decision,
/// not a per-associate one.
/// </summary>
[ApiController]
[Route("api/v1/business/stores/{storeId:guid}/service-area")]
[Authorize(Policy = AuthorizationPolicies.FranchiseeOwner)]
public sealed class BusinessStoreServiceAreaController(
    IHandler<BusinessGetServiceAreaQuery, Result<AdminServiceAreaDto>> getHandler) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(AdminServiceAreaDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid storeId, CancellationToken cancellationToken)
        => this.ToActionResult(await getHandler.HandleAsync(
            new BusinessGetServiceAreaQuery(storeId),
            cancellationToken));
}
