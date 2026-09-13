using System.Security.Claims;
using Chevrere.Modules.Identity.Domain;
using Chevrere.SharedKernel.Context;
using Microsoft.AspNetCore.Http;

namespace Chevrere.Infrastructure.Context;

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public const string TenantIdClaim = "tenant_id";

    private IReadOnlyCollection<string>? _roles;

    public bool IsAuthenticated => accessor.HttpContext?.User.Identity?.IsAuthenticated == true;

    public Guid? UserId => ParseGuid(accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier));

    public Guid? TenantId => ParseGuid(accessor.HttpContext?.User.FindFirstValue(TenantIdClaim));

    public string? Email => accessor.HttpContext?.User.FindFirstValue(ClaimTypes.Email);

    public IReadOnlyCollection<string> Roles => _roles ??= ReadRoles();

    public bool IsPlatformUser => Roles.Any(RoleNames.IsPlatformRole);

    private IReadOnlyCollection<string> ReadRoles() =>
        accessor.HttpContext?.User.FindAll(ClaimTypes.Role).Select(static c => c.Value).ToArray()
        ?? [];

    private static Guid? ParseGuid(string? value) =>
        Guid.TryParse(value, out var parsed) ? parsed : null;
}
