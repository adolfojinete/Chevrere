using YaaJuu.Infrastructure.Persistence;
using YaaJuu.Modules.Identity.Domain;
using YaaJuu.Modules.Tenancy.Application.Abstractions;
using YaaJuu.Modules.Tenancy.Application.Contracts;
using YaaJuu.Modules.Tenancy.Domain;
using YaaJuu.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace YaaJuu.Modules.Tenancy.Infrastructure.Persistence;

public sealed class TenancyStore(YaaJuuDbContext dbContext) : ITenancyStore
{
    public Task<bool> TenantCodeExistsAsync(string code, CancellationToken cancellationToken) =>
        dbContext.Tenants.AnyAsync(t => t.Code == code, cancellationToken);

    public Task<bool> FranchiseeCodeExistsAsync(string code, CancellationToken cancellationToken) =>
        dbContext.Franchisees.AnyAsync(f => f.Code == code, cancellationToken);

    public Task<bool> IdentificationExistsAsync(string identificationNumber, CancellationToken cancellationToken) =>
        dbContext.Franchisees.AnyAsync(f => f.IdentificationNumber == identificationNumber, cancellationToken);

    public Task<bool> StoreCodeExistsAsync(Guid tenantId, string code, CancellationToken cancellationToken) =>
        dbContext.Stores.AnyAsync(s => s.TenantId == tenantId && s.Code == code, cancellationToken);

    public void AddTenant(Tenant tenant) => dbContext.Tenants.Add(tenant);

    public void AddFranchisee(Franchisee franchisee) => dbContext.Franchisees.Add(franchisee);

    public void AddStore(Store store) => dbContext.Stores.Add(store);

    public Task<Franchisee?> GetFranchiseeAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Franchisees.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

    public Task<Tenant?> GetTenantAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Tenants.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Store>> ListStoresByFranchiseeAsync(
        Guid franchiseeId,
        CancellationToken cancellationToken)
    {
        return await dbContext.Stores
            .Where(s => s.FranchiseeId == franchiseeId)
            .OrderBy(s => s.Code)
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<FranchiseeListItemDto>> ListFranchiseesAsync(
        FranchiseeListQuery query,
        CancellationToken cancellationToken)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var franchisees = dbContext.Franchisees.AsNoTracking().AsQueryable();
        if (query.Status is { } status)
        {
            franchisees = franchisees.Where(f => f.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            franchisees = franchisees.Where(f =>
                EF.Functions.ILike(f.TradeName, $"%{term}%") ||
                EF.Functions.ILike(f.LegalName, $"%{term}%") ||
                EF.Functions.ILike(f.IdentificationNumber, $"%{term}%") ||
                EF.Functions.ILike(f.Code, $"%{term}%"));
        }

        var joined =
            from franchisee in franchisees
            join tenant in dbContext.Tenants.AsNoTracking() on franchisee.TenantId equals tenant.Id
            join subscription in dbContext.Subscriptions.AsNoTracking()
                on franchisee.TenantId equals subscription.TenantId into subscriptionGroup
            from subscription in subscriptionGroup.DefaultIfEmpty()
            select new { franchisee, tenant, subscription };

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            joined = joined.Where(x =>
                EF.Functions.ILike(x.franchisee.TradeName, $"%{term}%") ||
                EF.Functions.ILike(x.franchisee.LegalName, $"%{term}%") ||
                EF.Functions.ILike(x.franchisee.IdentificationNumber, $"%{term}%") ||
                EF.Functions.ILike(x.franchisee.Code, $"%{term}%") ||
                EF.Functions.ILike(x.tenant.Code, $"%{term}%"));
        }

        var total = await joined.CountAsync(cancellationToken);
        var items = await joined
            .OrderByDescending(x => x.franchisee.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new FranchiseeListItemDto(
                x.franchisee.Id,
                x.franchisee.TenantId,
                x.tenant.Code,
                x.franchisee.Code,
                x.franchisee.TradeName,
                x.franchisee.LegalName,
                x.franchisee.IdentificationNumber,
                x.franchisee.Status,
                x.subscription == null ? null : x.subscription.Status,
                x.franchisee.CreatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<FranchiseeListItemDto>(items, page, pageSize, total);
    }

    public async Task<FranchiseeDetailDto?> GetFranchiseeDetailAsync(Guid id, CancellationToken cancellationToken)
    {
        var franchisee = await dbContext.Franchisees.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (franchisee is null)
        {
            return null;
        }

        var tenant = await dbContext.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == franchisee.TenantId, cancellationToken);
        if (tenant is null)
        {
            return null;
        }

        var owner = await (
            from user in dbContext.Users.AsNoTracking()
            join userRole in dbContext.UserRoles on user.Id equals userRole.UserId
            join role in dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
            where user.TenantId == franchisee.TenantId && role.Name == RoleNames.FranchiseeOwner
            select new OwnerSummaryDto(user.Id, user.Email!, user.DisplayName)
        ).FirstOrDefaultAsync(cancellationToken);

        var subscription = await (
            from sub in dbContext.Subscriptions.AsNoTracking()
            join plan in dbContext.Plans.AsNoTracking() on sub.PlanId equals plan.Id
            where sub.TenantId == franchisee.TenantId
            orderby sub.CreatedAt descending
            select new SubscriptionSummaryDto(
                sub.Id,
                plan.Code,
                sub.Status,
                sub.CurrentPeriodEnd,
                sub.SuspendedAt,
                sub.SuspensionReason)
        ).FirstOrDefaultAsync(cancellationToken);

        var stores = await dbContext.Stores.AsNoTracking()
            .Where(s => s.FranchiseeId == franchisee.Id)
            .OrderBy(s => s.Code)
            .Select(s => new StoreDto(
                s.Id,
                s.TenantId,
                s.FranchiseeId,
                s.Code,
                s.Name,
                s.Status,
                s.AddressInternal,
                s.Latitude,
                s.Longitude,
                s.CreatedAt))
            .ToListAsync(cancellationToken);

        return new FranchiseeDetailDto(
            franchisee.Id,
            franchisee.TenantId,
            tenant.Code,
            tenant.Status,
            franchisee.Code,
            franchisee.LegalName,
            franchisee.TradeName,
            franchisee.IdentificationType,
            franchisee.IdentificationNumber,
            franchisee.Email,
            franchisee.Phone,
            franchisee.Status,
            franchisee.CreatedAt,
            franchisee.UpdatedAt,
            owner,
            subscription,
            stores);
    }

    public async Task<IReadOnlyList<AuditEventDto>> ListAuditEventsAsync(
        Guid? tenantId,
        Guid? franchiseeId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.AuditEvents.AsNoTracking().AsQueryable();
        if (tenantId is Guid tid)
        {
            query = query.Where(e => e.TenantId == tid);
        }

        if (franchiseeId is Guid fid)
        {
            var franchisee = await dbContext.Franchisees.AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == fid, cancellationToken);
            if (franchisee is not null)
            {
                query = query.Where(e =>
                    e.TenantId == franchisee.TenantId ||
                    (e.EntityType == nameof(Franchisee) && e.EntityId == fid));
            }
        }

        return await query
            .OrderByDescending(e => e.OccurredAt)
            .Take(200)
            .Select(e => new AuditEventDto(
                e.Id,
                e.TenantId,
                e.ActorUserId,
                e.Action,
                e.EntityType,
                e.EntityId,
                e.OccurredAt,
                e.CorrelationId,
                e.PreviousValue,
                e.NewValue))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StoreDto>> ListStoresForCurrentTenantAsync(
        Guid? storeId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Stores.AsNoTracking().AsQueryable();
        if (storeId is Guid id)
        {
            query = query.Where(s => s.Id == id);
        }

        return await query
            .OrderBy(s => s.Code)
            .Select(s => new StoreDto(
                s.Id,
                s.TenantId,
                s.FranchiseeId,
                s.Code,
                s.Name,
                s.Status,
                s.AddressInternal,
                s.Latitude,
                s.Longitude,
                s.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}
