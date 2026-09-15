using YaaJuu.Infrastructure.Identity;
using YaaJuu.Modules.Identity.Application.Abstractions;
using YaaJuu.Modules.Identity.Application.Contracts;
using YaaJuu.SharedKernel.Results;
using Microsoft.AspNetCore.Identity;

namespace YaaJuu.Modules.Identity.Infrastructure.Authentication;

public sealed class UserAuthenticator(
    UserManager<ApplicationUser> userManager,
    ITokenIssuer tokenIssuer) : IUserAuthenticator
{
    public async Task<Result<LoginResponse>> AuthenticateAsync(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null || !user.IsActive)
        {
            return Result.Failure<LoginResponse>(
                Error.Unauthorized(ErrorCodes.Unauthorized, "Invalid credentials."));
        }

        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            return Result.Failure<LoginResponse>(
                Error.Unauthorized(ErrorCodes.Unauthorized, "Invalid credentials."));
        }

        var roles = await userManager.GetRolesAsync(user);
        var token = tokenIssuer.Issue(new TokenIssueRequest(
            user.Id,
            user.Email!,
            user.TenantId,
            user.DisplayName,
            roles.ToArray()));

        return Result.Success(new LoginResponse(
            token.AccessToken,
            token.ExpiresAt,
            user.Id,
            user.Email!,
            user.TenantId,
            roles.ToArray()));
    }
}
