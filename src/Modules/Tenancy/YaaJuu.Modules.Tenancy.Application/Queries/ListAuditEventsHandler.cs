using YaaJuu.Modules.Tenancy.Application.Abstractions;
using YaaJuu.Modules.Tenancy.Application.Contracts;
using YaaJuu.SharedKernel.Application;

namespace YaaJuu.Modules.Tenancy.Application.Queries;

public sealed record ListAuditEventsQuery(Guid? TenantId, Guid? FranchiseeId);

public sealed class ListAuditEventsHandler(ITenancyStore store)
    : IHandler<ListAuditEventsQuery, IReadOnlyList<AuditEventDto>>
{
    public Task<IReadOnlyList<AuditEventDto>> HandleAsync(
        ListAuditEventsQuery request,
        CancellationToken cancellationToken)
        => store.ListAuditEventsAsync(request.TenantId, request.FranchiseeId, cancellationToken);
}
