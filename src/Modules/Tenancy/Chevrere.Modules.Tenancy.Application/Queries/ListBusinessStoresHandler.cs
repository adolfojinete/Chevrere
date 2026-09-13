using Chevrere.Modules.Tenancy.Application.Abstractions;
using Chevrere.Modules.Tenancy.Application.Contracts;
using Chevrere.SharedKernel.Application;
using Chevrere.SharedKernel.Context;
using Chevrere.SharedKernel.Results;

namespace Chevrere.Modules.Tenancy.Application.Queries;

public sealed record ListBusinessStoresQuery(Guid? StoreId);

public sealed class ListBusinessStoresHandler(ITenancyStore store, ICurrentUser currentUser)
    : IHandler<ListBusinessStoresQuery, Result<IReadOnlyList<StoreDto>>>
{
    public async Task<Result<IReadOnlyList<StoreDto>>> HandleAsync(
        ListBusinessStoresQuery request,
        CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null)
        {
            return Result.Failure<IReadOnlyList<StoreDto>>(
                Error.Forbidden(ErrorCodes.Forbidden, "The current user is not bound to a tenant."));
        }

        var stores = await store.ListStoresForCurrentTenantAsync(request.StoreId, cancellationToken);
        if (request.StoreId is not null && stores.Count == 0)
        {
            return Result.Failure<IReadOnlyList<StoreDto>>(Error.NotFound(ErrorCodes.NotFound, "Store not found."));
        }

        return Result.Success(stores);
    }
}
