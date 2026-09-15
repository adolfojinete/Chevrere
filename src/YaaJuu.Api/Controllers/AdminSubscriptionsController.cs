using System.Diagnostics.CodeAnalysis;
using YaaJuu.Api.Http;
using YaaJuu.Modules.Identity.Domain;
using YaaJuu.Modules.Subscriptions.Application.Commands;
using YaaJuu.Modules.Subscriptions.Application.Contracts;
using YaaJuu.Modules.Subscriptions.Application.Queries;
using YaaJuu.SharedKernel.Application;
using YaaJuu.SharedKernel.Results;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace YaaJuu.Api.Controllers;

[ApiController]
[Route("api/v1/admin/tenants/{tenantId:guid}/subscription")]
[Authorize(Policy = AuthorizationPolicies.PlatformStaff)]
[SuppressMessage("csharpsquid", "S6960", Justification = "REST resource: read and change-plan are the same subscription resource.")]
public sealed class AdminSubscriptionsController(
    IHandler<GetSubscriptionByTenantQuery, Result<SubscriptionDto>> getHandler,
    IHandler<ChangePlanCommand, Result> changePlanHandler,
    IValidator<ChangePlanCommand> changePlanValidator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(SubscriptionDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid tenantId, CancellationToken cancellationToken)
    {
        var result = await getHandler.HandleAsync(new GetSubscriptionByTenantQuery(tenantId), cancellationToken);
        return this.ToActionResult(result);
    }

    [HttpPost("change-plan")]
    [Authorize(Policy = AuthorizationPolicies.PlatformOperators)]
    public async Task<IActionResult> ChangePlan(
        Guid tenantId,
        [FromBody] ChangePlanRequest request,
        CancellationToken cancellationToken)
    {
        var command = new ChangePlanCommand(tenantId, request.PlanCode);
        var validation = await changePlanValidator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return this.ToValidationProblem(validation);
        }

        var result = await changePlanHandler.HandleAsync(command, cancellationToken);
        return this.ToActionResult(result);
    }
}
