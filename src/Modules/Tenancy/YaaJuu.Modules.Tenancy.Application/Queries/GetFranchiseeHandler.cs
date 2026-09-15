using YaaJuu.Modules.Tenancy.Application.Abstractions;
using YaaJuu.Modules.Tenancy.Application.Contracts;
using YaaJuu.SharedKernel.Application;
using YaaJuu.SharedKernel.Results;

namespace YaaJuu.Modules.Tenancy.Application.Queries;

public sealed record GetFranchiseeQuery(Guid Id);

public sealed class GetFranchiseeHandler(ITenancyStore store)
    : IHandler<GetFranchiseeQuery, Result<FranchiseeDetailDto>>
{
    public async Task<Result<FranchiseeDetailDto>> HandleAsync(
        GetFranchiseeQuery request,
        CancellationToken cancellationToken)
    {
        var detail = await store.GetFranchiseeDetailAsync(request.Id, cancellationToken);
        return detail is null
            ? Result.Failure<FranchiseeDetailDto>(Error.NotFound(ErrorCodes.NotFound, "Franchisee not found."))
            : Result.Success(detail);
    }
}
