using YaaJuu.Modules.Payments.Application.Abstractions;
using YaaJuu.Modules.Payments.Application.Commands;
using YaaJuu.Modules.Payments.Application.Contracts;
using YaaJuu.Modules.Payments.Domain;
using YaaJuu.SharedKernel.Application;
using YaaJuu.SharedKernel.Context;
using YaaJuu.SharedKernel.Results;

namespace YaaJuu.Modules.Payments.Application.Queries;

public sealed record GetConsumerPaymentQuery(Guid OrderId);

public sealed class GetConsumerPaymentHandler(
    IPaymentStore store,
    IPaymentSecretProtector secrets,
    ICurrentUser currentUser,
    YaaJuu.SharedKernel.Payments.IOrderPaymentLifecycle orders)
    : IHandler<GetConsumerPaymentQuery, Result<ConsumerPaymentDto>>
{
    public async Task<Result<ConsumerPaymentDto>> HandleAsync(
        GetConsumerPaymentQuery request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            return Result.Failure<ConsumerPaymentDto>(Error.Unauthorized("auth.required", "Authentication required."));
        }

        var payable = await orders.GetPayableOrderAsync(request.OrderId, currentUser.UserId.Value, cancellationToken);
        if (payable is null)
        {
            return Result.Failure<ConsumerPaymentDto>(Error.NotFound("payment.not_found", "Payment not found."));
        }

        var payment = await store.GetByOrderIdAsync(request.OrderId, cancellationToken);
        if (payment is null)
        {
            return Result.Failure<ConsumerPaymentDto>(Error.NotFound("payment.not_found", "Payment not found."));
        }

        return Result.Success(await InitializePaymentHandler.MapConsumerAsync(payment, store, secrets, cancellationToken));
    }
}

public sealed record GetAdminMerchantQuery(Guid TenantId);

public sealed class GetAdminMerchantHandler(IPaymentStore store)
    : IHandler<GetAdminMerchantQuery, Result<WompiMerchantConfigurationDto>>
{
    public async Task<Result<WompiMerchantConfigurationDto>> HandleAsync(
        GetAdminMerchantQuery request,
        CancellationToken cancellationToken)
    {
        var merchants = await store.ListMerchantsForTenantAsync(request.TenantId, cancellationToken);
        var latest = merchants.OrderByDescending(m => m.Version).FirstOrDefault();
        if (latest is null)
        {
            return Result.Failure<WompiMerchantConfigurationDto>(
                Error.NotFound("payment.merchant.not_configured", "Merchant configuration not found."));
        }

        return Result.Success(ConfigureWompiMerchantHandler.Map(latest));
    }
}

public sealed record GetBusinessPaymentConfigurationQuery;

public sealed class GetBusinessPaymentConfigurationHandler(
    IPaymentStore store,
    ICurrentUser currentUser)
    : IHandler<GetBusinessPaymentConfigurationQuery, Result<BusinessPaymentConfigurationStatusDto>>
{
    public async Task<Result<BusinessPaymentConfigurationStatusDto>> HandleAsync(
        GetBusinessPaymentConfigurationQuery request,
        CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null)
        {
            return Result.Failure<BusinessPaymentConfigurationStatusDto>(
                Error.Forbidden("tenant.required", "Tenant context required."));
        }

        var active = await store.GetActiveMerchantAsync(
            currentUser.TenantId.Value, PaymentProvider.Wompi, cancellationToken);
        if (active is null)
        {
            var any = await store.ListMerchantsForTenantAsync(currentUser.TenantId.Value, cancellationToken);
            var latest = any.OrderByDescending(m => m.Version).FirstOrDefault();
            return Result.Success(new BusinessPaymentConfigurationStatusDto(
                latest is not null,
                false,
                latest?.Provider.ToString(),
                latest?.Environment.ToString()));
        }

        return Result.Success(new BusinessPaymentConfigurationStatusDto(
            true,
            active.IsEnabled,
            active.Provider.ToString(),
            active.Environment.ToString()));
    }
}
