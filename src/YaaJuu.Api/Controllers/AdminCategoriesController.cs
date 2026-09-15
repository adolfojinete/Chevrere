using System.Diagnostics.CodeAnalysis;
using YaaJuu.Api.Http;
using YaaJuu.Infrastructure.Persistence;
using YaaJuu.SharedKernel.Persistence;
using YaaJuu.Modules.Catalog.Application.Commands;
using YaaJuu.Modules.Catalog.Application.Contracts;
using YaaJuu.Modules.Catalog.Application.Queries;
using YaaJuu.Modules.Catalog.Domain;
using YaaJuu.Modules.Identity.Domain;
using YaaJuu.SharedKernel.Application;
using YaaJuu.SharedKernel.Results;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace YaaJuu.Api.Controllers;

[ApiController]
[Route("api/v1/admin/categories")]
[Authorize(Policy = AuthorizationPolicies.PlatformStaff)]
[SuppressMessage("csharpsquid", "S6960", Justification = "REST resource: category list, detail and lifecycle stay together.")]
public sealed class AdminCategoriesController(
    IHandler<CreateCategoryRequest, Result<CategoryDto>> createHandler,
    IHandler<UpdateCategoryCommand, Result<CategoryDto>> updateHandler,
    IHandler<ActivateCategoryCommand, Result> activateHandler,
    IHandler<DeactivateCategoryCommand, Result> deactivateHandler,
    IHandler<GetCategoryQuery, Result<CategoryDto>> getHandler,
    IHandler<CategoryListQuery, PagedResult<CategoryDto>> listHandler,
    IValidator<CreateCategoryRequest> createValidator,
    IValidator<UpdateCategoryRequest> updateValidator) : ControllerBase
{
    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.PlatformOperators)]
    [ProducesResponseType(typeof(CategoryDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateCategoryRequest request, CancellationToken cancellationToken)
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
    [ProducesResponseType(typeof(PagedResult<CategoryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] CategoryStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        return Ok(await listHandler.HandleAsync(new CategoryListQuery(page, pageSize, search, status), cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(CategoryDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
        => this.ToActionResult(await getHandler.HandleAsync(new GetCategoryQuery(id), cancellationToken));

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.PlatformOperators)]
    [ProducesResponseType(typeof(CategoryDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await updateValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return this.ToValidationProblem(validation);
        }

        try
        {
            return this.ToActionResult(await updateHandler.HandleAsync(new UpdateCategoryCommand(id, request), cancellationToken));
        }
        catch (ConcurrencyConflictException)
        {
            return this.FromConcurrency();
        }
    }

    [HttpPost("{id:guid}/activate")]
    [Authorize(Policy = AuthorizationPolicies.PlatformOperators)]
    public async Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken)
        => this.ToActionResult(await activateHandler.HandleAsync(new ActivateCategoryCommand(id), cancellationToken));

    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Policy = AuthorizationPolicies.PlatformOperators)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
        => this.ToActionResult(await deactivateHandler.HandleAsync(new DeactivateCategoryCommand(id), cancellationToken));
}
