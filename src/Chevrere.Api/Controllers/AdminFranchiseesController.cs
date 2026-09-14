using System.Diagnostics.CodeAnalysis;
using Chevrere.Api.Http;
using Chevrere.Infrastructure.Persistence;
using Chevrere.SharedKernel.Persistence;
using Chevrere.Modules.Identity.Domain;
using Chevrere.Modules.Tenancy.Application.Commands.ActivateFranchisee;
using Chevrere.Modules.Tenancy.Application.Commands.ReactivateFranchisee;
using Chevrere.Modules.Tenancy.Application.Commands.SuspendFranchisee;
using Chevrere.Modules.Tenancy.Application.Contracts;
using Chevrere.Modules.Tenancy.Application.Queries;
using Chevrere.Modules.Tenancy.Domain;
using Chevrere.SharedKernel.Application;
using Chevrere.SharedKernel.Results;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Chevrere.Api.Controllers;

[ApiController]
[Route("api/v1/admin/franchisees")]
[Authorize(Policy = AuthorizationPolicies.PlatformStaff)]
[SuppressMessage("csharpsquid", "S6960", Justification = "REST resource: list, detail, lifecycle and nested stores belong to the franchisee aggregate.")]
public sealed class AdminFranchiseesController(
    IHandler<CreateFranchiseeRequest, Result<CreateFranchiseeResponse>> createHandler,
    IHandler<ActivateFranchiseeCommand, Result> activateHandler,
    IHandler<SuspendFranchiseeCommand, Result> suspendHandler,
    IHandler<ReactivateFranchiseeCommand, Result> reactivateHandler,
    IHandler<GetFranchiseeQuery, Result<FranchiseeDetailDto>> getHandler,
    IHandler<FranchiseeListQuery, PagedResult<FranchiseeListItemDto>> listHandler,
    IHandler<ListFranchiseeStoresQuery, Result<IReadOnlyList<StoreDto>>> storesHandler,
    IValidator<CreateFranchiseeRequest> createValidator) : ControllerBase
{
    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.PlatformOperators)]
    [ProducesResponseType(typeof(CreateFranchiseeResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(
        [FromBody] CreateFranchiseeRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await createValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return this.ToValidationProblem(validation);
        }

        try
        {
            var result = await createHandler.HandleAsync(request, cancellationToken);
            return this.ToCreatedResult(result, nameof(GetById), new { id = result.IsSuccess ? result.Value.FranchiseeId : Guid.Empty });
        }
        catch (ConcurrencyConflictException)
        {
            return this.FromConcurrency();
        }
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<FranchiseeListItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] FranchiseeStatus? status = null,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        var result = await listHandler.HandleAsync(new FranchiseeListQuery(page, pageSize, status, search), cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(FranchiseeDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await getHandler.HandleAsync(new GetFranchiseeQuery(id), cancellationToken);
        return this.ToActionResult(result);
    }

    [HttpPost("{id:guid}/activate")]
    [Authorize(Policy = AuthorizationPolicies.PlatformOperators)]
    public async Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await activateHandler.HandleAsync(new ActivateFranchiseeCommand(id), cancellationToken);
            return this.ToActionResult(result);
        }
        catch (ConcurrencyConflictException)
        {
            return this.FromConcurrency();
        }
    }

    [HttpPost("{id:guid}/suspend")]
    [Authorize(Policy = AuthorizationPolicies.PlatformOperators)]
    public async Task<IActionResult> Suspend(
        Guid id,
        [FromBody] SuspendFranchiseeRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await suspendHandler.HandleAsync(new SuspendFranchiseeCommand(id, request.Reason), cancellationToken);
            return this.ToActionResult(result);
        }
        catch (ConcurrencyConflictException)
        {
            return this.FromConcurrency();
        }
    }

    [HttpPost("{id:guid}/reactivate")]
    [Authorize(Policy = AuthorizationPolicies.PlatformOperators)]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await reactivateHandler.HandleAsync(new ReactivateFranchiseeCommand(id), cancellationToken);
            return this.ToActionResult(result);
        }
        catch (ConcurrencyConflictException)
        {
            return this.FromConcurrency();
        }
    }

    [HttpGet("{id:guid}/stores")]
    [ProducesResponseType(typeof(IReadOnlyList<StoreDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListStores(Guid id, CancellationToken cancellationToken)
    {
        var result = await storesHandler.HandleAsync(new ListFranchiseeStoresQuery(id), cancellationToken);
        return this.ToActionResult(result);
    }
}
