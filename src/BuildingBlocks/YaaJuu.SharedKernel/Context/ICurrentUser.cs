namespace YaaJuu.SharedKernel.Context;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    Guid? UserId { get; }

    Guid? TenantId { get; }

    string? Email { get; }

    bool IsPlatformUser { get; }

    IReadOnlyCollection<string> Roles { get; }
}
