using YaaJuu.Modules.Tenancy.Application.Abstractions;
using YaaJuu.Modules.Tenancy.Application.Contracts;
using YaaJuu.SharedKernel.Application;
using YaaJuu.SharedKernel.Results;

namespace YaaJuu.Modules.Tenancy.Application.Queries;

public sealed class ListFranchiseesHandler(ITenancyStore store)
    : IHandler<FranchiseeListQuery, PagedResult<FranchiseeListItemDto>>
{
    public Task<PagedResult<FranchiseeListItemDto>> HandleAsync(
        FranchiseeListQuery request,
        CancellationToken cancellationToken)
        => store.ListFranchiseesAsync(request, cancellationToken);
}
