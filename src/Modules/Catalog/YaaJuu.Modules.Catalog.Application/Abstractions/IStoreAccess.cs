namespace YaaJuu.Modules.Catalog.Application.Abstractions;

public interface IStoreAccess
{
    Task<Guid?> GetStoreTenantIdAsync(Guid storeId, CancellationToken cancellationToken);
}
