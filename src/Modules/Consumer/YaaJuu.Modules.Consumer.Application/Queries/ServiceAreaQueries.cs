using YaaJuu.Modules.Consumer.Application.Abstractions;
using YaaJuu.Modules.Consumer.Application.Contracts;
using YaaJuu.SharedKernel.Application;
using YaaJuu.SharedKernel.Context;
using YaaJuu.SharedKernel.Results;

namespace YaaJuu.Modules.Consumer.Application.Queries;

public sealed record AdminGetServiceAreaQuery(Guid StoreId);

public sealed class AdminGetServiceAreaHandler(IStoreServiceAreaStore store)
    : IHandler<AdminGetServiceAreaQuery, Result<AdminServiceAreaDto>>
{
    public async Task<Result<AdminServiceAreaDto>> HandleAsync(
        AdminGetServiceAreaQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var area = await store.GetByStoreAsync(request.StoreId, cancellationToken);
        return area is null
            ? Result.Failure<AdminServiceAreaDto>(Error.NotFound(ErrorCodes.NotFound, "Service area not found."))
            : Result.Success(ConsumerMapping.ToDto(area));
    }
}

public sealed record BusinessGetServiceAreaQuery(Guid StoreId);

/// <summary>
/// An owner sees the area of their own store. A store of another tenant is reported as missing, not
/// as forbidden: a 403 would confirm the store exists.
/// </summary>
public sealed class BusinessGetServiceAreaHandler(IStoreServiceAreaStore store, ICurrentUser currentUser)
    : IHandler<BusinessGetServiceAreaQuery, Result<AdminServiceAreaDto>>
{
    public async Task<Result<AdminServiceAreaDto>> HandleAsync(
        BusinessGetServiceAreaQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.TenantId is not Guid tenantId)
        {
            return Result.Failure<AdminServiceAreaDto>(
                Error.Forbidden(ErrorCodes.Forbidden, "The current user is not bound to a tenant."));
        }

        var area = await store.GetByStoreAsync(request.StoreId, cancellationToken);
        return area is null || !area.BelongsTo(tenantId)
            ? Result.Failure<AdminServiceAreaDto>(Error.NotFound(ErrorCodes.NotFound, "Service area not found."))
            : Result.Success(ConsumerMapping.ToDto(area));
    }
}
