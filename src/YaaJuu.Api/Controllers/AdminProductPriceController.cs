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
[Route("api/v1/admin/products/{productId:guid}/price")]
[Authorize(Policy = AuthorizationPolicies.PlatformStaff)]
[SuppressMessage("csharpsquid", "S6960", Justification = "REST resource: current price and history stay together.")]
public sealed class AdminProductPriceController(
    IHandler<GetGlobalSuggestedPriceQuery, Result<GlobalPriceDto>> getHandler,
    IHandler<SetGlobalSuggestedPriceCommand, Result<GlobalPriceDto>> setHandler,
    IHandler<GlobalPriceHistoryQuery, Result<PagedResult<PriceHistoryItemDto>>> historyHandler,
    IValidator<SetPriceRequest> setValidator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(GlobalPriceDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid productId, CancellationToken cancellationToken)
        => this.ToActionResult(await getHandler.HandleAsync(new GetGlobalSuggestedPriceQuery(productId), cancellationToken));

    [HttpPut]
    [Authorize(Policy = AuthorizationPolicies.PlatformOperators)]
    [ProducesResponseType(typeof(GlobalPriceDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Set(
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
                new SetGlobalSuggestedPriceCommand(productId, request.Amount, request.Currency),
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

    [HttpGet("history")]
    [ProducesResponseType(typeof(PagedResult<PriceHistoryItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> History(
        Guid productId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => this.ToActionResult(await historyHandler.HandleAsync(
            new GlobalPriceHistoryQuery(productId, page, pageSize),
            cancellationToken));
}
