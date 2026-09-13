using Chevrere.Modules.Tenancy.Application.Abstractions;
using Chevrere.Modules.Tenancy.Application.Contracts;
using Chevrere.SharedKernel.Application;

namespace Chevrere.Modules.Tenancy.Application.Queries;

public sealed record ListAuditEventsQuery(Guid? TenantId, Guid? FranchiseeId);

public sealed class ListAuditEventsHandler(ITenancyStore store)
    : IHandler<ListAuditEventsQuery, IReadOnlyList<AuditEventDto>>
{
    public Task<IReadOnlyList<AuditEventDto>> HandleAsync(
        ListAuditEventsQuery request,
        CancellationToken cancellationToken)
        => store.ListAuditEventsAsync(request.TenantId, request.FranchiseeId, cancellationToken);
}
