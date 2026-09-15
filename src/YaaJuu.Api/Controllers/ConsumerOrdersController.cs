using System.Diagnostics.CodeAnalysis;
using YaaJuu.Api.Http;
using YaaJuu.Modules.Identity.Domain;
using YaaJuu.Modules.Orders.Application.Commands;
using YaaJuu.Modules.Orders.Application.Contracts;
using YaaJuu.Modules.Orders.Application.Queries;
using YaaJuu.SharedKernel.Application;
using YaaJuu.SharedKernel.Idempotency;
using YaaJuu.SharedKernel.Results;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace YaaJuu.Api.Controllers;

[ApiController]
[Route("api/v1/consumer/orders")]
[Authorize(Policy = AuthorizationPolicies.Consumer)]
[SuppressMessage("csharpsquid", "S6960", Justification = "REST resource: consumer orders stay together.")]
public sealed class ConsumerOrdersController(
    IHandler<CreateOrderCommand, Result<ConsumerOrderDto>> createHandler,
    IHandler<CancelOrderCommand, Result<ConsumerOrderDto>> cancelHandler,
    IHandler<ListConsumerOrdersQuery, Result<PagedResult<ConsumerOrderSummaryDto>>> listHandler,
    IHandler<GetConsumerOrderQuery, Result<ConsumerOrderDto>> getHandler,
    IValidator<CreateOrderRequest> createValidator,
    IValidator<CancelOrderRequest> cancelValidator) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(ConsumerOrderDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(
        [FromBody] CreateOrderRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var validation = await createValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return this.ToValidationProblem(validation);
        }

        if (!IdempotencyKeyRules.IsValid(idempotencyKey))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "orders.idempotency_key.required",
                Detail = "Idempotency-Key header is required (8-128 chars, no whitespace).",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var result = await createHandler.HandleAsync(
            new CreateOrderCommand(request.Latitude, request.Longitude, idempotencyKey!),
            cancellationToken);
        return this.ToCreatedResult(result, nameof(GetById), new { id = result.IsSuccess ? result.Value.Id : Guid.Empty });
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ConsumerOrderSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        this.ToActionResult(await listHandler.HandleAsync(
            new ListConsumerOrdersQuery(page, pageSize), cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ConsumerOrderDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await getHandler.HandleAsync(new GetConsumerOrderQuery(id), cancellationToken));

    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(typeof(ConsumerOrderDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Cancel(
        Guid id,
        [FromBody] CancelOrderRequest? request,
        CancellationToken cancellationToken)
    {
        request ??= new CancelOrderRequest(null);
        var validation = await cancelValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return this.ToValidationProblem(validation);
        }

        return this.ToActionResult(await cancelHandler.HandleAsync(
            new CancelOrderCommand(id, request.Reason), cancellationToken));
    }
}
