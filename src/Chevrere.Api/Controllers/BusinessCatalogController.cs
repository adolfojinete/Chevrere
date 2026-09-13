using Chevrere.Api.Http;
using Chevrere.Modules.Catalog.Application.Contracts;
using Chevrere.Modules.Catalog.Application.Queries;
using Chevrere.Modules.Identity.Domain;
using Chevrere.SharedKernel.Application;
using Chevrere.SharedKernel.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Chevrere.Api.Controllers;

[ApiController]
[Route("api/v1/business/catalog/products")]
[Authorize(Policy = AuthorizationPolicies.FranchiseeOwner)]
public sealed class BusinessCatalogController(
    IHandler<BrowseCatalogQuery, Result<PagedResult<CatalogProductDto>>> handler) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<CatalogProductDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Browse(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] Guid? categoryId = null,
        [FromQuery] string? sortBy = null,
        CancellationToken cancellationToken = default)
    {
        var result = await handler.HandleAsync(
            new BrowseCatalogQuery(page, pageSize, search, categoryId, sortBy),
            cancellationToken);
        return this.ToActionResult(result);
    }
}
