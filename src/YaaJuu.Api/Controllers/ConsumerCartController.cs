using System.Diagnostics.CodeAnalysis;
using YaaJuu.Api.Http;
using YaaJuu.Modules.Identity.Domain;
using YaaJuu.Modules.Orders.Application.Commands;
using YaaJuu.Modules.Orders.Application.Contracts;
using YaaJuu.SharedKernel.Application;
using YaaJuu.SharedKernel.Results;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace YaaJuu.Api.Controllers;

[ApiController]
[Route("api/v1/consumer/cart")]
[Authorize(Policy = AuthorizationPolicies.Consumer)]
[SuppressMessage("csharpsquid", "S6960", Justification = "REST resource: the consumer cart stays together.")]
public sealed class ConsumerCartController(
    IHandler<ViewCartQuery, Result<CartViewDto>> viewHandler,
    IHandler<PutCartItemCommand, Result<CartViewDto>> putHandler,
    IHandler<RemoveCartItemCommand, Result<CartViewDto>> removeHandler,
    IHandler<ClearCartCommand, Result<CartViewDto>> clearHandler,
    IValidator<OrderLocationRequest> locationValidator,
    IValidator<ConsumerLocationQuantityRequest> quantityValidator) : ControllerBase
{
    [HttpPost("view")]
    [ProducesResponseType(typeof(CartViewDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> View(
        [FromBody] OrderLocationRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await locationValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return this.ToValidationProblem(validation);
        }

        return this.ToActionResult(await viewHandler.HandleAsync(
            new ViewCartQuery(request.Latitude, request.Longitude),
            cancellationToken));
    }

    [HttpPut("items/{productId:guid}")]
    [ProducesResponseType(typeof(CartViewDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> PutItem(
        Guid productId,
        [FromBody] ConsumerLocationQuantityRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await quantityValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return this.ToValidationProblem(validation);
        }

        return this.ToActionResult(await putHandler.HandleAsync(
            new PutCartItemCommand(productId, request.Latitude, request.Longitude, request.Quantity),
            cancellationToken));
    }

    [HttpDelete("items/{productId:guid}")]
    [ProducesResponseType(typeof(CartViewDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> RemoveItem(Guid productId, CancellationToken cancellationToken) =>
        this.ToActionResult(await removeHandler.HandleAsync(new RemoveCartItemCommand(productId), cancellationToken));

    [HttpPost("clear")]
    [ProducesResponseType(typeof(CartViewDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Clear(CancellationToken cancellationToken) =>
        this.ToActionResult(await clearHandler.HandleAsync(new ClearCartCommand(), cancellationToken));
}
