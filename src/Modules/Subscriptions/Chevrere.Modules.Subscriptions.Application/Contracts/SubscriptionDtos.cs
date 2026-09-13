using Chevrere.Modules.Subscriptions.Domain;

namespace Chevrere.Modules.Subscriptions.Application.Contracts;

public sealed record PlanDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    decimal MonthlyPrice,
    string Currency,
    BillingPeriod BillingPeriod,
    bool IsActive);

public sealed record SubscriptionDto(
    Guid Id,
    Guid TenantId,
    Guid PlanId,
    string PlanCode,
    SubscriptionStatus Status,
    DateTimeOffset StartDate,
    DateTimeOffset CurrentPeriodStart,
    DateTimeOffset CurrentPeriodEnd,
    DateTimeOffset NextBillingDate,
    DateTimeOffset? GracePeriodUntil,
    DateTimeOffset? SuspendedAt,
    string? SuspensionReason);

public sealed record ChangePlanRequest(string PlanCode);
