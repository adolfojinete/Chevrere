using System.Diagnostics.CodeAnalysis;
using Chevrere.Api.Http;
using Chevrere.Infrastructure.Persistence;
using Chevrere.Modules.Catalog.Application.Commands;
using Chevrere.Modules.Catalog.Application.Contracts;
using Chevrere.Modules.Catalog.Application.Queries;
using Chevrere.Modules.Catalog.Domain;
using Chevrere.Modules.Identity.Domain;
using Chevrere.SharedKernel.Application;
using Chevrere.SharedKernel.Results;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Chevrere.Api.Controllers;

[ApiController]
[Route("api/v1/admin/products")]
[Authorize(Policy = AuthorizationPolicies.PlatformStaff)]
[SuppressMessage("csharpsquid", "S6960", Justification = "REST resource: global product list, detail and lifecycle stay together.")]
public sealed class AdminProductsController(
    IHandler<CreateGlobalProductRequest, Result<GlobalProductDto>> createHandler,
    IHandler<UpdateGlobalProductCommand, Result<GlobalProductDto>> updateHandler,
    IHandler<ActivateGlobalProductCommand, Result> activateHandler,
    IHandler<DeactivateGlobalProductCommand, Result> deactivateHandler,
    IHandler<GetGlobalProductQuery, Result<GlobalProductDto>> getHandler,
    IHandler<GlobalProductListQuery, PagedResult<GlobalProductDto>> listHandler,
    IValidator<CreateGlobalProductRequest> createValidator,
    IValidator<UpdateGlobalProductRequest> updateValidator) : ControllerBase
{
    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.PlatformOperators)]
    [ProducesResponseType(typeof(GlobalProductDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateGlobalProductRequest request, CancellationToken cancellationToken)
    {
        var validation = await createValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return this.ToValidationProblem(validation);
        }

        var result = await createHandler.HandleAsync(request, cancellationToken);
        return this.ToCreatedResult(result, nameof(GetById), new { id = result.IsSuccess ? result.Value.Id : Guid.Empty });
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<GlobalProductDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] Guid? categoryId = null,
        [FromQuery] GlobalProductStatus? status = null,
        [FromQuery] string? sortBy = null,
        CancellationToken cancellationToken = default)
    {
        return Ok(await listHandler.HandleAsync(
            new GlobalProductListQuery(page, pageSize, search, categoryId, status, sortBy),
            cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(GlobalProductDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
        => this.ToActionResult(await getHandler.HandleAsync(new GetGlobalProductQuery(id), cancellationToken));

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.PlatformOperators)]
    [ProducesResponseType(typeof(GlobalProductDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateGlobalProductRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await updateValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return this.ToValidationProblem(validation);
        }

        try
        {
            return this.ToActionResult(await updateHandler.HandleAsync(new UpdateGlobalProductCommand(id, request), cancellationToken));
        }
        catch (ConcurrencyConflictException)
        {
            return this.FromConcurrency();
        }
    }

    [HttpPost("{id:guid}/activate")]
    [Authorize(Policy = AuthorizationPolicies.PlatformOperators)]
    public async Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken)
        => this.ToActionResult(await activateHandler.HandleAsync(new ActivateGlobalProductCommand(id), cancellationToken));

    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Policy = AuthorizationPolicies.PlatformOperators)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
        => this.ToActionResult(await deactivateHandler.HandleAsync(new DeactivateGlobalProductCommand(id), cancellationToken));
}
