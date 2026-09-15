namespace YaaJuu.Modules.Identity.Application.Contracts;

public sealed record LoginResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    Guid UserId,
    string Email,
    Guid? TenantId,
    IReadOnlyCollection<string> Roles);
