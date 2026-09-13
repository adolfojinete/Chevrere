using Chevrere.Modules.Tenancy.Application.Abstractions;
using Chevrere.Modules.Tenancy.Application.Contracts;
using Chevrere.SharedKernel.Application;
using Chevrere.SharedKernel.Results;

namespace Chevrere.Modules.Tenancy.Application.Queries;

public sealed record ListFranchiseeStoresQuery(Guid FranchiseeId);

public sealed class ListFranchiseeStoresHandler(ITenancyStore store)
    : IHandler<ListFranchiseeStoresQuery, Result<IReadOnlyList<StoreDto>>>
{
    public async Task<Result<IReadOnlyList<StoreDto>>> HandleAsync(
        ListFranchiseeStoresQuery request,
        CancellationToken cancellationToken)
    {
        var franchisee = await store.GetFranchiseeAsync(request.FranchiseeId, cancellationToken);
        if (franchisee is null)
        {
            return Result.Failure<IReadOnlyList<StoreDto>>(Error.NotFound(ErrorCodes.NotFound, "Franchisee not found."));
        }

        var stores = await store.ListStoresByFranchiseeAsync(request.FranchiseeId, cancellationToken);
        var dtos = stores
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
            .ToArray();

        return Result.Success<IReadOnlyList<StoreDto>>(dtos);
    }
}
