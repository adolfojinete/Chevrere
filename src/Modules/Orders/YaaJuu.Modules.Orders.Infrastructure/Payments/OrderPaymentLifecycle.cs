using YaaJuu.Infrastructure.Persistence;
using YaaJuu.Modules.Orders.Domain;
using YaaJuu.Modules.Tenancy.Domain;
using YaaJuu.SharedKernel.Audit;
using YaaJuu.SharedKernel.Payments;
using YaaJuu.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace YaaJuu.Modules.Orders.Infrastructure.Payments;

/// <summary>
/// Stages Order confirmation inside the caller's DbContext/UoW. Does not call SaveChanges.
/// </summary>
public sealed class OrderPaymentLifecycle(
    YaaJuuDbContext dbContext,
    IAuditRecorder audit,
    IClock clock) : IOrderPaymentLifecycle
{
    public async Task<PayableOrderSnapshot?> GetPayableOrderAsync(
        Guid orderId,
        Guid consumerUserId,
        CancellationToken cancellationToken)
    {
        var order = await dbContext.Orders
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                o => o.Id == orderId && o.ConsumerUserId == consumerUserId,
                cancellationToken);
        return order is null ? null : await ToSnapshotAsync(order, cancellationToken);
    }

    public async Task<PayableOrderSnapshot?> GetOrderForPaymentAsync(
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var order = await dbContext.Orders
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);
        return order is null ? null : await ToSnapshotAsync(order, cancellationToken);
    }

    public async Task<ConfirmPaidOrderResult> ConfirmPaidOrderAsync(
        Guid orderId,
        Guid tenantId,
        Guid storeId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        _ = correlationId;

        var order = await dbContext.Orders
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                o => o.Id == orderId && o.TenantId == tenantId && o.StoreId == storeId,
                cancellationToken);

        if (order is null)
        {
            return new ConfirmPaidOrderResult(ConfirmPaidOrderOutcome.NotFound);
        }

        if (order.Status == OrderStatus.Confirmed)
        {
            return new ConfirmPaidOrderResult(ConfirmPaidOrderOutcome.AlreadyConfirmed);
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            return new ConfirmPaidOrderResult(ConfirmPaidOrderOutcome.Cancelled);
        }

        if (order.Status == OrderStatus.Expired)
        {
            return new ConfirmPaidOrderResult(ConfirmPaidOrderOutcome.Expired);
        }

        if (order.Status != OrderStatus.PendingPayment)
        {
            return new ConfirmPaidOrderResult(ConfirmPaidOrderOutcome.NotPendingPayment);
        }

        var mutated = order.Confirm(clock.UtcNow);
        if (!mutated)
        {
            return new ConfirmPaidOrderResult(ConfirmPaidOrderOutcome.AlreadyConfirmed);
        }

        audit.Record(
            AuditActions.OrderConfirmed,
            nameof(Order),
            order.Id,
            order.TenantId,
            previousValue: new { Status = OrderStatus.PendingPayment },
            newValue: new { order.Status, order.TotalAmount, order.Currency });

        return new ConfirmPaidOrderResult(ConfirmPaidOrderOutcome.Confirmed);
    }

    private async Task<PayableOrderSnapshot> ToSnapshotAsync(Order order, CancellationToken cancellationToken)
    {
        var tenantStatus = await dbContext.Tenants.IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.Id == order.TenantId)
            .Select(t => t.Status)
            .FirstAsync(cancellationToken);

        var storeStatus = await dbContext.Stores.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.Id == order.StoreId && s.TenantId == order.TenantId)
            .Select(s => s.Status)
            .FirstAsync(cancellationToken);

        return new PayableOrderSnapshot(
            order.Id,
            order.ConsumerUserId,
            order.TenantId,
            order.StoreId,
            order.Status.ToString(),
            order.TotalAmount,
            order.Currency,
            order.ExpiresAt,
            tenantStatus == TenantStatus.Active,
            storeStatus == StoreStatus.Active);
    }
}
