using System.Diagnostics.CodeAnalysis;
using YaaJuu.Api.Http;
using YaaJuu.Infrastructure.Persistence;
using YaaJuu.SharedKernel.Persistence;
using YaaJuu.Modules.Catalog.Application.Commands;
using YaaJuu.Modules.Catalog.Application.Contracts;
using YaaJuu.Modules.Catalog.Application.Queries;
using YaaJuu.Modules.Identity.Domain;
using YaaJuu.SharedKernel.Application;
using YaaJuu.SharedKernel.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace YaaJuu.Api.Controllers;

[ApiController]
[Route("api/v1/business/stores/{storeId:guid}/products")]
[Authorize(Policy = AuthorizationPolicies.FranchiseeOwner)]
[SuppressMessage("csharpsquid", "S6960", Justification = "REST resource: list, enable and disable belong to the store catalog.")]
public sealed class BusinessStoreProductsController(
    IHandler<ListStoreProductsQuery, Result<IReadOnlyList<StoreProductDto>>> listHandler,
    IHandler<EnableStoreProductCommand, Result<StoreProductDto>> enableHandler,
    IHandler<DisableStoreProductCommand, Result> disableHandler) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<StoreProductDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(Guid storeId, CancellationToken cancellationToken)
        => this.ToActionResult(await listHandler.HandleAsync(new ListStoreProductsQuery(storeId), cancellationToken));

    [HttpPost("{globalProductId:guid}/enable")]
    [ProducesResponseType(typeof(StoreProductDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Enable(Guid storeId, Guid globalProductId, CancellationToken cancellationToken)
    {
        try
        {
            return this.ToActionResult(
                await enableHandler.HandleAsync(new EnableStoreProductCommand(storeId, globalProductId), cancellationToken));
        }
        catch (ConcurrencyConflictException)
        {
            return this.FromConcurrency();
        }
    }

    [HttpPost("{globalProductId:guid}/disable")]
    public async Task<IActionResult> Disable(Guid storeId, Guid globalProductId, CancellationToken cancellationToken)
    {
        try
        {
            return this.ToActionResult(
                await disableHandler.HandleAsync(new DisableStoreProductCommand(storeId, globalProductId), cancellationToken));
        }
        catch (ConcurrencyConflictException)
        {
            return this.FromConcurrency();
        }
    }
}
