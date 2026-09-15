using YaaJuu.Modules.Payments.Application;
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
    ICurrentUser currentUser,
    Microsoft.Extensions.Options.IOptions<PaymentsOptions> options)
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

        var runtimeEnvironment = PaymentRuntimeEnvironment.Resolve(options.Value);
        var active = await store.GetActiveMerchantAsync(
            currentUser.TenantId.Value, PaymentProvider.Wompi, runtimeEnvironment, cancellationToken);
        if (active is null)
        {
            var any = await store.ListMerchantsForTenantAsync(currentUser.TenantId.Value, cancellationToken);
            var latestForRuntime = any
                .Where(m => m.Environment == runtimeEnvironment)
                .OrderByDescending(m => m.Version)
                .FirstOrDefault();
            return Result.Success(new BusinessPaymentConfigurationStatusDto(
                latestForRuntime is not null,
                false,
                latestForRuntime?.Provider.ToString(),
                runtimeEnvironment.ToString()));
        }

        return Result.Success(new BusinessPaymentConfigurationStatusDto(
            true,
            active.IsEnabled,
            active.Provider.ToString(),
            active.Environment.ToString()));
    }
}

public sealed record ListBusinessPaymentsQuery(Guid StoreId, int Page = 1, int PageSize = 20);

public sealed class ListBusinessPaymentsHandler(IPaymentStore store, ICurrentUser currentUser)
    : IHandler<ListBusinessPaymentsQuery, Result<PagedResult<BusinessPaymentSummaryDto>>>
{
    public async Task<Result<PagedResult<BusinessPaymentSummaryDto>>> HandleAsync(
        ListBusinessPaymentsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.TenantId is not Guid tenantId)
        {
            return Result.Failure<PagedResult<BusinessPaymentSummaryDto>>(
                Error.Forbidden("tenant.required", "Tenant context required."));
        }

        if (await store.GetStoreTenantIdAsync(request.StoreId, cancellationToken) is not Guid storeTenant
            || storeTenant != tenantId)
        {
            return Result.Failure<PagedResult<BusinessPaymentSummaryDto>>(
                Error.NotFound("payment.not_found", "Store not found."));
        }

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var (items, total) = await store.ListStorePaymentsAsync(
            tenantId, request.StoreId, page, pageSize, cancellationToken);
        var numbers = await store.GetOrderNumbersAsync(
            items.Select(p => p.OrderId).ToList(), cancellationToken);

        var summaries = items.Select(payment => new BusinessPaymentSummaryDto(
            payment.Id,
            payment.OrderId,
            numbers.GetValueOrDefault(payment.OrderId),
            payment.Amount,
            payment.Currency,
            payment.Status.ToString(),
            payment.RequiresReconciliation,
            payment.CreatedAt)).ToList();

        return Result.Success(new PagedResult<BusinessPaymentSummaryDto>(summaries, page, pageSize, total));
    }
}

public sealed record GetBusinessPaymentQuery(Guid StoreId, Guid PaymentId);

public sealed class GetBusinessPaymentHandler(IPaymentStore store, ICurrentUser currentUser)
    : IHandler<GetBusinessPaymentQuery, Result<BusinessPaymentDetailDto>>
{
    public async Task<Result<BusinessPaymentDetailDto>> HandleAsync(
        GetBusinessPaymentQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.TenantId is not Guid tenantId)
        {
            return Result.Failure<BusinessPaymentDetailDto>(
                Error.Forbidden("tenant.required", "Tenant context required."));
        }

        if (await store.GetStoreTenantIdAsync(request.StoreId, cancellationToken) is not Guid storeTenant
            || storeTenant != tenantId)
        {
            return Result.Failure<BusinessPaymentDetailDto>(
                Error.NotFound("payment.not_found", "Payment not found."));
        }

        var payment = await store.GetByIdForTenantStoreAsync(
            tenantId, request.StoreId, request.PaymentId, cancellationToken);
        if (payment is null)
        {
            return Result.Failure<BusinessPaymentDetailDto>(
                Error.NotFound("payment.not_found", "Payment not found."));
        }

        var orderNumber = await store.GetOrderNumberAsync(payment.OrderId, cancellationToken);
        return Result.Success(MapBusinessDetail(payment, orderNumber));
    }

    internal static BusinessPaymentDetailDto MapBusinessDetail(Payment payment, string? orderNumber) =>
        new(
            payment.Id,
            payment.OrderId,
            orderNumber,
            payment.Amount,
            payment.Currency,
            payment.Status.ToString(),
            payment.RequiresReconciliation,
            payment.RequiresReconciliation ? payment.ReconciliationReason.ToString() : null,
            payment.CreatedAt,
            payment.Attempts
                .OrderByDescending(a => a.CreatedAt)
                .Select(a => new PaymentAttemptSummaryDto(
                    a.Id,
                    a.Status.ToString(),
                    a.MerchantReference,
                    a.ProviderTransactionId,
                    a.CreatedAt,
                    a.ApprovedAt,
                    a.DeclinedAt))
                .ToList());
}

public sealed record ListAdminPaymentsQuery(
    int Page = 1,
    int PageSize = 20,
    Guid? TenantId = null,
    Guid? StoreId = null,
    string? Status = null,
    bool? RequiresReconciliation = null);

public sealed class ListAdminPaymentsHandler(IPaymentStore store)
    : IHandler<ListAdminPaymentsQuery, Result<PagedResult<AdminPaymentSummaryDto>>>
{
    public async Task<Result<PagedResult<AdminPaymentSummaryDto>>> HandleAsync(
        ListAdminPaymentsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        PaymentStatus? status = null;
        if (!string.IsNullOrWhiteSpace(request.Status)
            && Enum.TryParse<PaymentStatus>(request.Status, ignoreCase: true, out var parsed))
        {
            status = parsed;
        }

        var (items, total) = await store.ListAdminPaymentsAsync(
            page,
            pageSize,
            request.TenantId,
            request.StoreId,
            status,
            request.RequiresReconciliation,
            cancellationToken);

        return Result.Success(new PagedResult<AdminPaymentSummaryDto>(
            items.Select(p => new AdminPaymentSummaryDto(
                p.Id,
                p.OrderId,
                p.TenantId,
                p.StoreId,
                p.Amount,
                p.Currency,
                p.Status.ToString(),
                p.RequiresReconciliation,
                p.ReconciliationReason.ToString(),
                p.CreatedAt)).ToList(),
            page,
            pageSize,
            total));
    }
}

public sealed record GetAdminPaymentQuery(Guid PaymentId);

public sealed class GetAdminPaymentHandler(IPaymentStore store)
    : IHandler<GetAdminPaymentQuery, Result<AdminPaymentDetailDto>>
{
    public async Task<Result<AdminPaymentDetailDto>> HandleAsync(
        GetAdminPaymentQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var payment = await store.GetByIdAsync(request.PaymentId, cancellationToken);
        if (payment is null)
        {
            return Result.Failure<AdminPaymentDetailDto>(
                Error.NotFound("payment.not_found", "Payment not found."));
        }

        return Result.Success(new AdminPaymentDetailDto(
            payment.Id,
            payment.OrderId,
            payment.TenantId,
            payment.StoreId,
            payment.Provider.ToString(),
            payment.Amount,
            payment.Currency,
            payment.Status.ToString(),
            payment.RequiresReconciliation,
            payment.ReconciliationReason.ToString(),
            payment.CreatedAt,
            payment.ApprovedAt,
            payment.Attempts
                .OrderByDescending(a => a.CreatedAt)
                .Select(a => new PaymentAttemptSummaryDto(
                    a.Id,
                    a.Status.ToString(),
                    a.MerchantReference,
                    a.ProviderTransactionId,
                    a.CreatedAt,
                    a.ApprovedAt,
                    a.DeclinedAt))
                .ToList()));
    }
}
