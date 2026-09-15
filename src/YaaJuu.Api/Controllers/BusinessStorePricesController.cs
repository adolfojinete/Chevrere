using System.Diagnostics.CodeAnalysis;
using YaaJuu.Api.Http;
using YaaJuu.Infrastructure.Persistence;
using YaaJuu.Modules.Identity.Domain;
using YaaJuu.Modules.Pricing.Application.Commands;
using YaaJuu.Modules.Pricing.Application.Contracts;
using YaaJuu.Modules.Pricing.Application.Queries;
using YaaJuu.SharedKernel.Application;
using YaaJuu.SharedKernel.Persistence;
using YaaJuu.SharedKernel.Results;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace YaaJuu.Api.Controllers;

[ApiController]
[Route("api/v1/business/stores/{storeId:guid}")]
[Authorize(Policy = AuthorizationPolicies.FranchiseeOwner)]
[SuppressMessage("csharpsquid", "S6960", Justification = "REST resource: store prices and product price operations.")]
public sealed class BusinessStorePricesController(
    IHandler<GetStoreEffectivePriceQuery, Result<StoreEffectivePriceDto>> getHandler,
    IHandler<SetStoreOverridePriceCommand, Result<StoreEffectivePriceDto>> setHandler,
    IHandler<RemoveStoreOverridePriceCommand, Result> removeHandler,
    IHandler<StorePriceHistoryQuery, Result<PagedResult<PriceHistoryItemDto>>> historyHandler,
    IHandler<ListStorePricesQuery, Result<PagedResult<StorePriceListItemDto>>> listHandler,
    IValidator<SetPriceRequest> setValidator) : ControllerBase
{
    [HttpGet("prices")]
    [ProducesResponseType(typeof(PagedResult<StorePriceListItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        Guid storeId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => this.ToActionResult(await listHandler.HandleAsync(new ListStorePricesQuery(storeId, page, pageSize), cancellationToken));

    [HttpGet("products/{productId:guid}/price")]
    [ProducesResponseType(typeof(StoreEffectivePriceDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid storeId, Guid productId, CancellationToken cancellationToken)
        => this.ToActionResult(await getHandler.HandleAsync(new GetStoreEffectivePriceQuery(storeId, productId), cancellationToken));

    [HttpPut("products/{productId:guid}/price")]
    [ProducesResponseType(typeof(StoreEffectivePriceDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Set(
        Guid storeId,
        Guid productId,
        [FromBody] SetPriceRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await setValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return this.ToValidationProblem(validation);
        }

        try
        {
            return this.ToActionResult(await setHandler.HandleAsync(
                new SetStoreOverridePriceCommand(storeId, productId, request.Amount, request.Currency),
                cancellationToken));
        }
        catch (ConcurrencyConflictException)
        {
            return this.FromConcurrency();
        }
        catch (DuplicateKeyException)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Conflict",
                Detail = "Another concurrent price change won. Retry the request.",
                Status = StatusCodes.Status409Conflict
            });
        }
    }

    [HttpDelete("products/{productId:guid}/price")]
    public async Task<IActionResult> Remove(Guid storeId, Guid productId, CancellationToken cancellationToken)
    {
        try
        {
            return this.ToActionResult(await removeHandler.HandleAsync(
                new RemoveStoreOverridePriceCommand(storeId, productId),
                cancellationToken));
        }
        catch (ConcurrencyConflictException)
        {
            return this.FromConcurrency();
        }
    }

    [HttpGet("products/{productId:guid}/price/history")]
    [ProducesResponseType(typeof(PagedResult<PriceHistoryItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> History(
        Guid storeId,
        Guid productId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => this.ToActionResult(await historyHandler.HandleAsync(
            new StorePriceHistoryQuery(storeId, productId, page, pageSize),
            cancellationToken));
}
