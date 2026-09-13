using Chevrere.Modules.Subscriptions.Domain;
using Chevrere.Modules.Tenancy.Domain;

namespace Chevrere.Modules.Tenancy.Application.Contracts;

public sealed record CreateFranchiseeRequest(
    string TenantCode,
    string TenantName,
    string? FranchiseeCode,
    string LegalName,
    string TradeName,
    IdentificationType IdentificationType,
    string IdentificationNumber,
    string Email,
    string Phone,
    string OwnerEmail,
    string OwnerDisplayName,
    string OwnerPassword,
    string StoreCode,
    string StoreName,
    string AddressInternal,
    decimal? Latitude,
    decimal? Longitude,
    string PlanCode);

public sealed record CreateFranchiseeResponse(
    Guid TenantId,
    Guid FranchiseeId,
    Guid StoreId,
    Guid OwnerUserId,
    Guid SubscriptionId);

public sealed record FranchiseeListItemDto(
    Guid Id,
    Guid TenantId,
    string TenantCode,
    string Code,
    string TradeName,
    string LegalName,
    string IdentificationNumber,
    FranchiseeStatus Status,
    SubscriptionStatus? SubscriptionStatus,
    DateTimeOffset CreatedAt);

public sealed record FranchiseeDetailDto(
    Guid Id,
    Guid TenantId,
    string TenantCode,
    TenantStatus TenantStatus,
    string Code,
    string LegalName,
    string TradeName,
    IdentificationType IdentificationType,
    string IdentificationNumber,
    string Email,
    string Phone,
    FranchiseeStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    OwnerSummaryDto? Owner,
    SubscriptionSummaryDto? Subscription,
    IReadOnlyList<StoreDto> Stores);

public sealed record OwnerSummaryDto(Guid UserId, string Email, string DisplayName);

public sealed record SubscriptionSummaryDto(
    Guid Id,
    string PlanCode,
    SubscriptionStatus Status,
    DateTimeOffset CurrentPeriodEnd,
    DateTimeOffset? SuspendedAt,
    string? SuspensionReason);

public sealed record StoreDto(
    Guid Id,
    Guid TenantId,
    Guid FranchiseeId,
    string Code,
    string Name,
    StoreStatus Status,
    string AddressInternal,
    decimal? Latitude,
    decimal? Longitude,
    DateTimeOffset CreatedAt);

public sealed record FranchiseeListQuery(
    int Page,
    int PageSize,
    FranchiseeStatus? Status,
    string? Search);

public sealed record SuspendFranchiseeRequest(string Reason);

public sealed record AuditEventDto(
    Guid Id,
    Guid? TenantId,
    Guid? ActorUserId,
    string Action,
    string EntityType,
    Guid EntityId,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    string? PreviousValue,
    string? NewValue);
