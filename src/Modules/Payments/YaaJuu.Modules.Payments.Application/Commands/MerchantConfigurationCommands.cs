using YaaJuu.Modules.Payments.Application.Abstractions;
using YaaJuu.Modules.Payments.Application.Contracts;
using YaaJuu.Modules.Payments.Domain;
using YaaJuu.SharedKernel.Application;
using YaaJuu.SharedKernel.Audit;
using YaaJuu.SharedKernel.Domain;
using YaaJuu.SharedKernel.Persistence;
using YaaJuu.SharedKernel.Results;
using YaaJuu.SharedKernel.Time;

namespace YaaJuu.Modules.Payments.Application.Commands;

public sealed record ConfigureWompiMerchantCommand(Guid TenantId, WompiMerchantWriteRequest Request);

public sealed class ConfigureWompiMerchantHandler(
    IPaymentStore store,
    IPaymentSecretProtector secrets,
    IAuditRecorder audit,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IHandler<ConfigureWompiMerchantCommand, Result<WompiMerchantConfigurationDto>>
{
    public async Task<Result<WompiMerchantConfigurationDto>> HandleAsync(
        ConfigureWompiMerchantCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = request.Request;
        if (!Enum.TryParse<MerchantEnvironment>(body.Environment, ignoreCase: true, out var environment))
        {
            return Result.Failure<WompiMerchantConfigurationDto>(
                Error.Validation("payment.merchant.environment", "Environment must be Sandbox or Production."));
        }

        if (string.IsNullOrWhiteSpace(body.PublicKey))
        {
            return Result.Failure<WompiMerchantConfigurationDto>(
                Error.Validation("payment.merchant.public_key", "PublicKey is required."));
        }

        var latest = await store.GetLatestMerchantAsync(
            request.TenantId, PaymentProvider.Wompi, environment, cancellationToken);

        if (latest is null || body.ReplaceSecrets)
        {
            if (string.IsNullOrWhiteSpace(body.PrivateKey)
                || string.IsNullOrWhiteSpace(body.IntegritySecret)
                || string.IsNullOrWhiteSpace(body.EventsSecret))
            {
                return Result.Failure<WompiMerchantConfigurationDto>(
                    Error.Validation("payment.merchant.secrets_required", "All Wompi secrets are required."));
            }

            if (latest is { IsEnabled: true } && latest.Disable(clock.UtcNow))
            {
                audit.Record(
                    AuditActions.MerchantConfigurationDisabled,
                    nameof(PaymentMerchantConfiguration),
                    latest.Id,
                    latest.TenantId,
                    newValue: new { latest.Version, latest.Environment });
            }

            var version = (latest?.Version ?? 0) + 1;
            var created = PaymentMerchantConfiguration.Create(
                request.TenantId,
                PaymentProvider.Wompi,
                environment,
                body.PublicKey.Trim(),
                secrets.Protect(body.PrivateKey!, PaymentSecretPurposes.WompiPrivateKey),
                secrets.Protect(body.IntegritySecret!, PaymentSecretPurposes.WompiIntegritySecret),
                secrets.Protect(body.EventsSecret!, PaymentSecretPurposes.WompiEventsSecret),
                version,
                clock.UtcNow);
            store.Add(created);
            audit.Record(
                AuditActions.MerchantConfigurationCreated,
                nameof(PaymentMerchantConfiguration),
                created.Id,
                created.TenantId,
                newValue: new { created.Version, created.Environment, created.IsEnabled });
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success(Map(created));
        }

        if (!string.Equals(latest.PublicKey, body.PublicKey.Trim(), StringComparison.Ordinal))
        {
            latest.ReplaceSecrets(
                body.PublicKey.Trim(),
                latest.EncryptedPrivateKey,
                latest.EncryptedIntegritySecret,
                latest.EncryptedEventsSecret,
                clock.UtcNow);
            audit.Record(
                AuditActions.MerchantConfigurationUpdated,
                nameof(PaymentMerchantConfiguration),
                latest.Id,
                latest.TenantId,
                newValue: new { latest.Version, PublicKeyUpdated = true });
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(Map(latest));
    }

    internal static WompiMerchantConfigurationDto Map(PaymentMerchantConfiguration c) =>
        new(
            c.Provider.ToString(),
            c.Environment.ToString(),
            c.IsEnabled,
            Mask(c.PublicKey),
            true,
            true,
            true,
            c.Version,
            c.UpdatedAt);

    private static string Mask(string publicKey)
    {
        if (publicKey.Length <= 8)
        {
            return "****";
        }

        return $"{publicKey[..4]}…{publicKey[^4..]}";
    }
}

public sealed record EnableWompiMerchantCommand(Guid TenantId, string Environment);

public sealed class EnableWompiMerchantHandler(
    IPaymentStore store,
    IAuditRecorder audit,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IHandler<EnableWompiMerchantCommand, Result<WompiMerchantConfigurationDto>>
{
    public async Task<Result<WompiMerchantConfigurationDto>> HandleAsync(
        EnableWompiMerchantCommand request,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<MerchantEnvironment>(request.Environment, true, out var environment))
        {
            return Result.Failure<WompiMerchantConfigurationDto>(
                Error.Validation("payment.merchant.environment", "Environment must be Sandbox or Production."));
        }

        var latest = await store.GetLatestMerchantAsync(
            request.TenantId, PaymentProvider.Wompi, environment, cancellationToken);
        if (latest is null)
        {
            return Result.Failure<WompiMerchantConfigurationDto>(
                Error.NotFound("payment.merchant.not_configured", "Merchant configuration not found."));
        }

        try
        {
            if (latest.Enable(clock.UtcNow))
            {
                audit.Record(
                    AuditActions.MerchantConfigurationEnabled,
                    nameof(PaymentMerchantConfiguration),
                    latest.Id,
                    latest.TenantId,
                    newValue: new { latest.Version, latest.Environment });
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
        }
        catch (DomainException ex)
        {
            return Result.Failure<WompiMerchantConfigurationDto>(Error.Conflict(ex.Code, ex.Message));
        }

        return Result.Success(ConfigureWompiMerchantHandler.Map(latest));
    }
}

public sealed record DisableWompiMerchantCommand(Guid TenantId, string Environment);

public sealed class DisableWompiMerchantHandler(
    IPaymentStore store,
    IAuditRecorder audit,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IHandler<DisableWompiMerchantCommand, Result<WompiMerchantConfigurationDto>>
{
    public async Task<Result<WompiMerchantConfigurationDto>> HandleAsync(
        DisableWompiMerchantCommand request,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<MerchantEnvironment>(request.Environment, true, out var environment))
        {
            return Result.Failure<WompiMerchantConfigurationDto>(
                Error.Validation("payment.merchant.environment", "Environment must be Sandbox or Production."));
        }

        var latest = await store.GetLatestMerchantAsync(
            request.TenantId, PaymentProvider.Wompi, environment, cancellationToken);
        if (latest is null)
        {
            return Result.Failure<WompiMerchantConfigurationDto>(
                Error.NotFound("payment.merchant.not_configured", "Merchant configuration not found."));
        }

        if (latest.Disable(clock.UtcNow))
        {
            audit.Record(
                AuditActions.MerchantConfigurationDisabled,
                nameof(PaymentMerchantConfiguration),
                latest.Id,
                latest.TenantId,
                newValue: new { latest.Version, latest.Environment });
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(ConfigureWompiMerchantHandler.Map(latest));
    }
}
