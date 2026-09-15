using System.Diagnostics.CodeAnalysis;
using YaaJuu.Api.Http;
using YaaJuu.Modules.Identity.Domain;
using YaaJuu.Modules.Procurement.Application.Commands;
using YaaJuu.Modules.Procurement.Application.Contracts;
using YaaJuu.Modules.Procurement.Application.Queries;
using YaaJuu.SharedKernel.Application;
using YaaJuu.SharedKernel.Persistence;
using YaaJuu.SharedKernel.Results;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace YaaJuu.Api.Controllers;

[ApiController]
[Route("api/v1/business/suppliers")]
[Authorize(Policy = AuthorizationPolicies.FranchiseeOwner)]
[SuppressMessage("csharpsquid", "S6960", Justification = "REST resource: supplier list, detail and lifecycle stay together.")]
public sealed class BusinessSuppliersController(
    IHandler<CreateSupplierRequest, Result<SupplierDto>> createHandler,
    IHandler<UpdateSupplierCommand, Result<SupplierDto>> updateHandler,
    IHandler<ActivateSupplierCommand, Result> activateHandler,
    IHandler<DeactivateSupplierCommand, Result> deactivateHandler,
    IHandler<ListSuppliersQuery, Result<PagedResult<SupplierDto>>> listHandler,
    IHandler<GetSupplierQuery, Result<SupplierDto>> getHandler,
    IValidator<CreateSupplierRequest> createValidator,
    IValidator<UpdateSupplierRequest> updateValidator) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(SupplierDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(
        [FromBody] CreateSupplierRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await createValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return this.ToValidationProblem(validation);
        }

        var result = await createHandler.HandleAsync(request, cancellationToken);
        return this.ToCreatedResult(
            result,
            nameof(GetById),
            new { id = result.IsSuccess ? result.Value.Id : Guid.Empty });
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<SupplierDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] bool? isActive = null,
        CancellationToken cancellationToken = default)
        => this.ToActionResult(await listHandler.HandleAsync(
            new ListSuppliersQuery(page, pageSize, search, isActive),
            cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(SupplierDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
        => this.ToActionResult(await getHandler.HandleAsync(new GetSupplierQuery(id), cancellationToken));

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(SupplierDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateSupplierRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await updateValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return this.ToValidationProblem(validation);
        }

        try
        {
            return this.ToActionResult(
                await updateHandler.HandleAsync(new UpdateSupplierCommand(id, request), cancellationToken));
        }
        catch (ConcurrencyConflictException)
        {
            return this.FromConcurrency();
        }
    }

    [HttpPost("{id:guid}/activate")]
    public async Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken)
        => this.ToActionResult(await activateHandler.HandleAsync(new ActivateSupplierCommand(id), cancellationToken));

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
        => this.ToActionResult(await deactivateHandler.HandleAsync(new DeactivateSupplierCommand(id), cancellationToken));
}
