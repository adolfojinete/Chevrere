using System.Diagnostics.CodeAnalysis;
using YaaJuu.Api.Http;
using YaaJuu.Modules.Identity.Domain;
using YaaJuu.Modules.Procurement.Application.Commands;
using YaaJuu.Modules.Procurement.Application.Contracts;
using YaaJuu.Modules.Procurement.Application.Queries;
using YaaJuu.Modules.Procurement.Domain;
using YaaJuu.SharedKernel.Application;
using YaaJuu.SharedKernel.Idempotency;
using YaaJuu.SharedKernel.Persistence;
using YaaJuu.SharedKernel.Results;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace YaaJuu.Api.Controllers;

[ApiController]
[Route("api/v1/business/stores/{storeId:guid}/purchase-orders")]
[Authorize(Policy = AuthorizationPolicies.FranchiseeOwner)]
[SuppressMessage("csharpsquid", "S6960", Justification = "REST resource: purchase orders, their lifecycle and their receipts stay together.")]
public sealed class BusinessPurchaseOrdersController(
    IHandler<CreatePurchaseOrderCommand, Result<PurchaseOrderDto>> createHandler,
    IHandler<UpdatePurchaseOrderDraftCommand, Result<PurchaseOrderDto>> updateHandler,
    IHandler<ApprovePurchaseOrderCommand, Result<PurchaseOrderDto>> approveHandler,
    IHandler<CancelPurchaseOrderCommand, Result<PurchaseOrderDto>> cancelHandler,
    IHandler<ListPurchaseOrdersQuery, Result<PagedResult<PurchaseOrderSummaryDto>>> listHandler,
    IHandler<GetPurchaseOrderQuery, Result<PurchaseOrderDto>> getHandler,
    IHandler<ReceiveGoodsCommand, Result<GoodsReceiptDto>> receiveHandler,
    IHandler<ListGoodsReceiptsQuery, Result<PagedResult<GoodsReceiptSummaryDto>>> receiptsHandler,
    IHandler<GetGoodsReceiptQuery, Result<GoodsReceiptDto>> receiptHandler,
    IValidator<CreatePurchaseOrderRequest> createValidator,
    IValidator<UpdatePurchaseOrderRequest> updateValidator,
    IValidator<CancelPurchaseOrderRequest> cancelValidator,
    IValidator<ReceiveGoodsRequest> receiveValidator) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(PurchaseOrderDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(
        Guid storeId,
        [FromBody] CreatePurchaseOrderRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var validation = await createValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return this.ToValidationProblem(validation);
        }

        if (!TryValidateIdempotencyKey(idempotencyKey, out var key, out var problem))
        {
            return problem!;
        }

        var result = await ExecuteAsync(() => createHandler.HandleAsync(
            new CreatePurchaseOrderCommand(storeId, request.SupplierId, request.Notes, request.Items, key),
            cancellationToken));

        if (result.Failure is not null)
        {
            return result.Failure;
        }

        return this.ToCreatedResult(
            result.Value!,
            nameof(GetById),
            new { storeId, id = result.Value!.IsSuccess ? result.Value.Value.Id : Guid.Empty });
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<PurchaseOrderSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        Guid storeId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] PurchaseOrderStatus? status = null,
        [FromQuery] Guid? supplierId = null,
        CancellationToken cancellationToken = default)
        => this.ToActionResult(await listHandler.HandleAsync(
            new ListPurchaseOrdersQuery(storeId, page, pageSize, status, supplierId),
            cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(PurchaseOrderDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(Guid storeId, Guid id, CancellationToken cancellationToken)
        => this.ToActionResult(await getHandler.HandleAsync(new GetPurchaseOrderQuery(storeId, id), cancellationToken));

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(PurchaseOrderDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateDraft(
        Guid storeId,
        Guid id,
        [FromBody] UpdatePurchaseOrderRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await updateValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return this.ToValidationProblem(validation);
        }

        var result = await ExecuteAsync(() => updateHandler.HandleAsync(
            new UpdatePurchaseOrderDraftCommand(storeId, id, request), cancellationToken));
        return result.Failure ?? this.ToActionResult(result.Value!);
    }

    [HttpPost("{id:guid}/approve")]
    [ProducesResponseType(typeof(PurchaseOrderDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Approve(Guid storeId, Guid id, CancellationToken cancellationToken)
    {
        var result = await ExecuteAsync(() => approveHandler.HandleAsync(
            new ApprovePurchaseOrderCommand(storeId, id), cancellationToken));
        return result.Failure ?? this.ToActionResult(result.Value!);
    }

    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(typeof(PurchaseOrderDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Cancel(
        Guid storeId,
        Guid id,
        [FromBody] CancelPurchaseOrderRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await cancelValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return this.ToValidationProblem(validation);
        }

        var result = await ExecuteAsync(() => cancelHandler.HandleAsync(
            new CancelPurchaseOrderCommand(storeId, id, request.Reason), cancellationToken));
        return result.Failure ?? this.ToActionResult(result.Value!);
    }

    [HttpPost("{id:guid}/receipts")]
    [ProducesResponseType(typeof(GoodsReceiptDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Receive(
        Guid storeId,
        Guid id,
        [FromBody] ReceiveGoodsRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var validation = await receiveValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return this.ToValidationProblem(validation);
        }

        if (!TryValidateIdempotencyKey(idempotencyKey, out var key, out var problem))
        {
            return problem!;
        }

        var result = await ExecuteAsync(() => receiveHandler.HandleAsync(
            new ReceiveGoodsCommand(storeId, id, request.Notes, request.Lines, key),
            cancellationToken));
        return result.Failure ?? this.ToActionResult(result.Value!);
    }

    [HttpGet("{id:guid}/receipts")]
    [ProducesResponseType(typeof(PagedResult<GoodsReceiptSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListReceipts(
        Guid storeId,
        Guid id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => this.ToActionResult(await receiptsHandler.HandleAsync(
            new ListGoodsReceiptsQuery(storeId, id, page, pageSize),
            cancellationToken));

    [HttpGet("{id:guid}/receipts/{receiptId:guid}")]
    [ProducesResponseType(typeof(GoodsReceiptDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetReceipt(
        Guid storeId,
        Guid id,
        Guid receiptId,
        CancellationToken cancellationToken)
        => this.ToActionResult(await receiptHandler.HandleAsync(
            new GetGoodsReceiptQuery(storeId, receiptId),
            cancellationToken));

    /// <summary>
    /// Turns the persistence exceptions a use case chose not to recover from into ProblemDetails.
    /// </summary>
    private async Task<(Result<T>? Value, IActionResult? Failure)> ExecuteAsync<T>(Func<Task<Result<T>>> action)
    {
        try
        {
            return (await action(), null);
        }
        catch (ConcurrencyConflictException)
        {
            return (null, this.FromConcurrency());
        }
        catch (DuplicateKeyException)
        {
            return (null, Conflict(new ProblemDetails
            {
                Title = ErrorCodes.Conflict,
                Detail = "A concurrent procurement write conflicted. Retry with a new Idempotency-Key if needed.",
                Status = StatusCodes.Status409Conflict
            }));
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
                Title = "procurement.idempotency_key.required",
                Detail = "Idempotency-Key header is required (8-128 chars, no whitespace).",
                Status = StatusCodes.Status400BadRequest
            });
            return false;
        }

        key = idempotencyKey!;
        return true;
    }
}
