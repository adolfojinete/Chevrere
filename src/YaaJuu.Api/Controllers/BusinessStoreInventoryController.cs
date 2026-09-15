using System.Diagnostics.CodeAnalysis;
using YaaJuu.Api.Http;
using YaaJuu.Infrastructure.Persistence;
using YaaJuu.Modules.Identity.Domain;
using YaaJuu.Modules.Inventory.Application.Commands;
using YaaJuu.Modules.Inventory.Application.Contracts;
using YaaJuu.Modules.Inventory.Application.Queries;
using YaaJuu.SharedKernel.Application;
using YaaJuu.SharedKernel.Idempotency;
using YaaJuu.SharedKernel.Persistence;
using YaaJuu.SharedKernel.Results;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace YaaJuu.Api.Controllers;

[ApiController]
[Route("api/v1/business/stores/{storeId:guid}/inventory")]
[Authorize(Policy = AuthorizationPolicies.FranchiseeOwner)]
[SuppressMessage("csharpsquid", "S6960", Justification = "REST resource: inventory balances and mutations stay together.")]
public sealed class BusinessStoreInventoryController(
    IHandler<ListStoreInventoryQuery, Result<PagedResult<InventoryItemDto>>> listHandler,
    IHandler<GetStoreInventoryItemQuery, Result<InventoryItemDto>> getHandler,
    IHandler<ListInventoryMovementsQuery, Result<PagedResult<InventoryMovementDto>>> movementsHandler,
    IHandler<InitializeInventoryCommand, Result<InventoryMutationDto>> initializeHandler,
    IHandler<AdjustInventoryCommand, Result<InventoryMutationDto>> adjustHandler,
    IHandler<WasteInventoryCommand, Result<InventoryMutationDto>> wasteHandler,
    IValidator<InitializeInventoryRequest> initializeValidator,
    IValidator<AdjustInventoryRequest> adjustValidator,
    IValidator<WasteInventoryRequest> wasteValidator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<InventoryItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        Guid storeId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] bool? initialized = null,
        [FromQuery] bool? enabled = null,
        [FromQuery] string? sortBy = null,
        CancellationToken cancellationToken = default)
        => this.ToActionResult(await listHandler.HandleAsync(
            new ListStoreInventoryQuery(storeId, page, pageSize, search, initialized, enabled, sortBy),
            cancellationToken));

    [HttpGet("{productId:guid}")]
    [ProducesResponseType(typeof(InventoryItemDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid storeId, Guid productId, CancellationToken cancellationToken)
        => this.ToActionResult(await getHandler.HandleAsync(new GetStoreInventoryItemQuery(storeId, productId), cancellationToken));

    [HttpGet("{productId:guid}/movements")]
    [ProducesResponseType(typeof(PagedResult<InventoryMovementDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Movements(
        Guid storeId,
        Guid productId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => this.ToActionResult(await movementsHandler.HandleAsync(
            new ListInventoryMovementsQuery(storeId, productId, page, pageSize),
            cancellationToken));

    [HttpPost("{productId:guid}/initialize")]
    [ProducesResponseType(typeof(InventoryMutationDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Initialize(
        Guid storeId,
        Guid productId,
        [FromBody] InitializeInventoryRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var validation = await initializeValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return this.ToValidationProblem(validation);
        }

        if (!TryValidateIdempotencyKey(idempotencyKey, out var key, out var problem))
        {
            return problem!;
        }

        return await ExecuteMutationAsync(() => initializeHandler.HandleAsync(
            new InitializeInventoryCommand(storeId, productId, request.Quantity, key),
            cancellationToken));
    }

    [HttpPost("{productId:guid}/adjustments")]
    [ProducesResponseType(typeof(InventoryMutationDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Adjust(
        Guid storeId,
        Guid productId,
        [FromBody] AdjustInventoryRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var validation = await adjustValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return this.ToValidationProblem(validation);
        }

        if (!TryValidateIdempotencyKey(idempotencyKey, out var key, out var problem))
        {
            return problem!;
        }

        return await ExecuteMutationAsync(() => adjustHandler.HandleAsync(
            new AdjustInventoryCommand(storeId, productId, request.Type, request.Quantity, request.Reason, key),
            cancellationToken));
    }

    [HttpPost("{productId:guid}/waste")]
    [ProducesResponseType(typeof(InventoryMutationDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Waste(
        Guid storeId,
        Guid productId,
        [FromBody] WasteInventoryRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var validation = await wasteValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return this.ToValidationProblem(validation);
        }

        if (!TryValidateIdempotencyKey(idempotencyKey, out var key, out var problem))
        {
            return problem!;
        }

        return await ExecuteMutationAsync(() => wasteHandler.HandleAsync(
            new WasteInventoryCommand(storeId, productId, request.Quantity, request.Reason, key),
            cancellationToken));
    }

    private async Task<IActionResult> ExecuteMutationAsync(Func<Task<Result<InventoryMutationDto>>> action)
    {
        try
        {
            return this.ToActionResult(await action());
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
                Detail = "A concurrent inventory write conflicted. Retry with a new Idempotency-Key if needed.",
                Status = StatusCodes.Status409Conflict
            });
        }
    }

    private bool TryValidateIdempotencyKey(string? idempotencyKey, out string key, out IActionResult? problem)
    {
        key = string.Empty;
        problem = null;
        if (!IdempotencyKeyRules.IsValid(idempotencyKey))
        {
            problem = BadRequest(new ProblemDetails
            {
                Title = "inventory.idempotency_key.required",
                Detail = "Idempotency-Key header is required (8-128 chars, no whitespace).",
                Status = StatusCodes.Status400BadRequest
            });
            return false;
        }

        key = idempotencyKey!;
        return true;
    }
}
