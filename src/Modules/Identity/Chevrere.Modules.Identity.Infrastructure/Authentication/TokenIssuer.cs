using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Chevrere.Infrastructure.Context;
using Chevrere.Infrastructure.Options;
using Chevrere.Modules.Identity.Application.Abstractions;
using Chevrere.SharedKernel.Time;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Chevrere.Modules.Identity.Infrastructure.Authentication;

public sealed class TokenIssuer(IOptions<JwtOptions> options, IClock clock) : ITokenIssuer
{
    public IssuedToken Issue(TokenIssueRequest request)
    {
        var jwt = options.Value;
        var now = clock.UtcNow;
        var expires = now.AddMinutes(jwt.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, request.UserId.ToString()),
            new(ClaimTypes.NameIdentifier, request.UserId.ToString()),
            new(ClaimTypes.Email, request.Email),
            new(JwtRegisteredClaimNames.Email, request.Email),
            new("display_name", request.DisplayName)
        };

        if (request.TenantId is Guid tenantId)
        {
            claims.Add(new Claim(HttpCurrentUser.TenantIdClaim, tenantId.ToString()));
        }

        claims.AddRange(request.Roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            jwt.Issuer,
            jwt.Audience,
            claims,
            now.UtcDateTime,
            expires.UtcDateTime,
            credentials);

        return new IssuedToken(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
