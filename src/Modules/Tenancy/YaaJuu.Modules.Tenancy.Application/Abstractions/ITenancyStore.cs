using YaaJuu.Modules.Tenancy.Application.Contracts;
using YaaJuu.Modules.Tenancy.Domain;
using YaaJuu.SharedKernel.Results;

namespace YaaJuu.Modules.Tenancy.Application.Abstractions;

public interface ITenancyStore
{
    Task<bool> TenantCodeExistsAsync(string code, CancellationToken cancellationToken);

    Task<bool> FranchiseeCodeExistsAsync(string code, CancellationToken cancellationToken);

    Task<bool> IdentificationExistsAsync(string identificationNumber, CancellationToken cancellationToken);

    Task<bool> StoreCodeExistsAsync(Guid tenantId, string code, CancellationToken cancellationToken);

    void AddTenant(Tenant tenant);

    void AddFranchisee(Franchisee franchisee);

    void AddStore(Store store);

    Task<Franchisee?> GetFranchiseeAsync(Guid id, CancellationToken cancellationToken);

    Task<Tenant?> GetTenantAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Store>> ListStoresByFranchiseeAsync(Guid franchiseeId, CancellationToken cancellationToken);

    Task<PagedResult<FranchiseeListItemDto>> ListFranchiseesAsync(
        FranchiseeListQuery query,
        CancellationToken cancellationToken);

    Task<FranchiseeDetailDto?> GetFranchiseeDetailAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<AuditEventDto>> ListAuditEventsAsync(
        Guid? tenantId,
        Guid? franchiseeId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<StoreDto>> ListStoresForCurrentTenantAsync(
        Guid? storeId,
        CancellationToken cancellationToken);
}
