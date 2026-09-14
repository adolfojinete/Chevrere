using System.Diagnostics.CodeAnalysis;
using Chevrere.Api.Http;
using Chevrere.Modules.Consumer.Application.Commands;
using Chevrere.Modules.Consumer.Application.Contracts;
using Chevrere.Modules.Consumer.Application.Queries;
using Chevrere.Modules.Identity.Domain;
using Chevrere.SharedKernel.Application;
using Chevrere.SharedKernel.Persistence;
using Chevrere.SharedKernel.Results;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Chevrere.Api.Controllers;

/// <summary>
/// Chevrere staff draw the delivery area of a dark store. Reading is staff-wide; drawing, enabling
/// and disabling it are operator decisions.
/// </summary>
[ApiController]
[Route("api/v1/admin/stores/{storeId:guid}/service-area")]
[Authorize(Policy = AuthorizationPolicies.PlatformStaff)]
[SuppressMessage("csharpsquid", "S6960", Justification = "REST resource: the service area of one store.")]
public sealed class AdminStoreServiceAreaController(
    IHandler<AdminGetServiceAreaQuery, Result<AdminServiceAreaDto>> getHandler,
    IHandler<ConfigureStoreServiceAreaCommand, Result<AdminServiceAreaDto>> configureHandler,
    IHandler<EnableStoreServiceAreaCommand, Result> enableHandler,
    IHandler<DisableStoreServiceAreaCommand, Result> disableHandler,
    IValidator<ConfigureServiceAreaRequest> configureValidator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(AdminServiceAreaDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid storeId, CancellationToken cancellationToken)
        => this.ToActionResult(await getHandler.HandleAsync(new AdminGetServiceAreaQuery(storeId), cancellationToken));

    [HttpPut]
    [Authorize(Policy = AuthorizationPolicies.PlatformOperators)]
    [ProducesResponseType(typeof(AdminServiceAreaDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Configure(
        Guid storeId,
        [FromBody] ConfigureServiceAreaRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await configureValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return this.ToValidationProblem(validation);
        }

        try
        {
            return this.ToActionResult(await configureHandler.HandleAsync(
                new ConfigureStoreServiceAreaCommand(storeId, request),
                cancellationToken));
        }
        catch (ConcurrencyConflictException)
        {
            return this.FromConcurrency();
        }
    }

    [HttpPost("enable")]
    [Authorize(Policy = AuthorizationPolicies.PlatformOperators)]
    public async Task<IActionResult> Enable(Guid storeId, CancellationToken cancellationToken)
    {
        try
        {
            return this.ToActionResult(await enableHandler.HandleAsync(
                new EnableStoreServiceAreaCommand(storeId),
                cancellationToken));
        }
        catch (ConcurrencyConflictException)
        {
            return this.FromConcurrency();
        }
    }

    [HttpPost("disable")]
    [Authorize(Policy = AuthorizationPolicies.PlatformOperators)]
    public async Task<IActionResult> Disable(Guid storeId, CancellationToken cancellationToken)
    {
        try
        {
            return this.ToActionResult(await disableHandler.HandleAsync(
                new DisableStoreServiceAreaCommand(storeId),
                cancellationToken));
        }
        catch (ConcurrencyConflictException)
        {
            return this.FromConcurrency();
        }
    }
}
