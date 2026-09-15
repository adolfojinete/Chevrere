namespace YaaJuu.Modules.Identity.Application.Abstractions;

public sealed record TokenIssueRequest(
    Guid UserId,
    string Email,
    Guid? TenantId,
    string DisplayName,
    IReadOnlyCollection<string> Roles);

public sealed record IssuedToken(string AccessToken, DateTimeOffset ExpiresAt);

public interface ITokenIssuer
{
    IssuedToken Issue(TokenIssueRequest request);
}
