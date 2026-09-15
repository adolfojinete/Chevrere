using System.Diagnostics.CodeAnalysis;
using YaaJuu.Api.Http;
using YaaJuu.Modules.Identity.Domain;
using YaaJuu.Modules.Payments.Application.Commands;
using YaaJuu.Modules.Payments.Application.Contracts;
using YaaJuu.Modules.Payments.Application.Queries;
using YaaJuu.SharedKernel.Application;
using YaaJuu.SharedKernel.Idempotency;
using YaaJuu.SharedKernel.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace YaaJuu.Api.Controllers;

[ApiController]
[Route("api/v1/consumer/orders/{orderId:guid}")]
[Authorize(Policy = AuthorizationPolicies.Consumer)]
[SuppressMessage("csharpsquid", "S6960", Justification = "Consumer payment subresource of orders.")]
public sealed class ConsumerPaymentsController(
    IHandler<InitializePaymentCommand, Result<ConsumerPaymentDto>> initializeHandler,
    IHandler<GetConsumerPaymentQuery, Result<ConsumerPaymentDto>> getHandler) : ControllerBase
{
    [HttpPost("payments")]
    [ProducesResponseType(typeof(ConsumerPaymentDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Initialize(
        Guid orderId,
        [FromBody] InitializePaymentRequest? body,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (!IdempotencyKeyRules.IsValid(idempotencyKey))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "payment.idempotency_key.required",
                Detail = "Idempotency-Key header is required (8-128 chars, no whitespace).",
                Status = StatusCodes.Status400BadRequest
            });
        }

        body ??= new InitializePaymentRequest(null, null, null, null, null);
        return this.ToActionResult(await initializeHandler.HandleAsync(
            new InitializePaymentCommand(orderId, idempotencyKey!, body),
            cancellationToken));
    }

    [HttpGet("payment")]
    [ProducesResponseType(typeof(ConsumerPaymentDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid orderId, CancellationToken cancellationToken) =>
        this.ToActionResult(await getHandler.HandleAsync(new GetConsumerPaymentQuery(orderId), cancellationToken));
}

[ApiController]
[Route("api/v1/webhooks/wompi")]
[AllowAnonymous]
public sealed class WompiWebhookController(
    IHandler<ProcessWompiWebhookCommand, Result<WebhookProcessOutcome>> handler) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(65_536)]
    [SuppressMessage("csharpsquid", "S6932", Justification = "Wompi checksum requires exact raw body bytes.")]
    public async Task<IActionResult> Receive(CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body);
        var raw = await reader.ReadToEndAsync(cancellationToken);
        var checksum = Request.Headers["X-Event-Checksum"].FirstOrDefault();
        var result = await handler.HandleAsync(new ProcessWompiWebhookCommand(raw, checksum), cancellationToken);
        if (!result.IsSuccess)
        {
            if (result.Error?.Code == "payment.webhook.invalid_signature")
            {
                return Unauthorized();
            }

            return this.ToActionResult(result);
        }

        return Ok();
    }
}

[ApiController]
[Route("api/v1/admin/tenants/{tenantId:guid}/payments/wompi")]
[Authorize(Policy = AuthorizationPolicies.PlatformStaff)]
[SuppressMessage("csharpsquid", "S6960", Justification = "Admin Wompi merchant configuration resource.")]
public sealed class AdminWompiMerchantController(
    IHandler<GetAdminMerchantQuery, Result<WompiMerchantConfigurationDto>> getHandler,
    IHandler<ConfigureWompiMerchantCommand, Result<WompiMerchantConfigurationDto>> configureHandler,
    IHandler<EnableWompiMerchantCommand, Result<WompiMerchantConfigurationDto>> enableHandler,
    IHandler<DisableWompiMerchantCommand, Result<WompiMerchantConfigurationDto>> disableHandler) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid tenantId, CancellationToken cancellationToken) =>
        this.ToActionResult(await getHandler.HandleAsync(new GetAdminMerchantQuery(tenantId), cancellationToken));

    [HttpPut]
    [Authorize(Policy = AuthorizationPolicies.PlatformOperators)]
    [RequestSizeLimit(16_384)]
    public async Task<IActionResult> Put(
        Guid tenantId,
        [FromBody] WompiMerchantWriteRequest request,
        CancellationToken cancellationToken) =>
        this.ToActionResult(await configureHandler.HandleAsync(
            new ConfigureWompiMerchantCommand(tenantId, request), cancellationToken));

    [HttpPost("enable")]
    [Authorize(Policy = AuthorizationPolicies.PlatformOperators)]
    public async Task<IActionResult> Enable(
        Guid tenantId,
        [FromQuery] string environment = "Sandbox",
        CancellationToken cancellationToken = default) =>
        this.ToActionResult(await enableHandler.HandleAsync(
            new EnableWompiMerchantCommand(tenantId, environment), cancellationToken));

    [HttpPost("disable")]
    [Authorize(Policy = AuthorizationPolicies.PlatformOperators)]
    public async Task<IActionResult> Disable(
        Guid tenantId,
        [FromQuery] string environment = "Sandbox",
        CancellationToken cancellationToken = default) =>
        this.ToActionResult(await disableHandler.HandleAsync(
            new DisableWompiMerchantCommand(tenantId, environment), cancellationToken));
}

[ApiController]
[Route("api/v1/business/payment-configuration")]
[Authorize(Policy = AuthorizationPolicies.FranchiseeOwner)]
public sealed class BusinessPaymentConfigurationController(
    IHandler<GetBusinessPaymentConfigurationQuery, Result<BusinessPaymentConfigurationStatusDto>> handler)
    : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        this.ToActionResult(await handler.HandleAsync(new GetBusinessPaymentConfigurationQuery(), cancellationToken));
}
