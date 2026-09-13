using Chevrere.Modules.Tenancy.Application.Abstractions;
using Chevrere.Modules.Tenancy.Application.Contracts;
using Chevrere.SharedKernel.Application;
using Chevrere.SharedKernel.Results;

namespace Chevrere.Modules.Tenancy.Application.Queries;

public sealed class ListFranchiseesHandler(ITenancyStore store)
    : IHandler<FranchiseeListQuery, PagedResult<FranchiseeListItemDto>>
{
    public Task<PagedResult<FranchiseeListItemDto>> HandleAsync(
        FranchiseeListQuery request,
        CancellationToken cancellationToken)
        => store.ListFranchiseesAsync(request, cancellationToken);
}
