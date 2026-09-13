namespace Chevrere.Modules.Identity.Application.Contracts;

public sealed record OwnerUserRequest(
    Guid TenantId,
    string Email,
    string DisplayName,
    string Password);
