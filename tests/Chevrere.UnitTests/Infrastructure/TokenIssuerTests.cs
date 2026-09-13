using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Chevrere.Infrastructure.Context;
using Chevrere.Infrastructure.Options;
using Chevrere.Modules.Identity.Application.Abstractions;
using Chevrere.Modules.Identity.Infrastructure.Authentication;
using Chevrere.SharedKernel.Time;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Chevrere.UnitTests.Infrastructure;

public sealed class TokenIssuerTests
{
    [Fact]
    public void Issues_jwt_with_and_without_tenant()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(FixedClock.Now);
        var options = Options.Create(new JwtOptions
        {
            Issuer = "chevrere",
            Audience = "chevrere-api",
            SigningKey = "INTEGRATION_TEST_SIGNING_KEY_32CH!!",
            AccessTokenMinutes = 30
        });
        var issuer = new TokenIssuer(options, clock);

        var platform = issuer.Issue(new TokenIssueRequest(
            Guid.CreateVersion7(),
            "admin@chevrere.test",
            null,
            "Admin",
            ["PlatformSuperAdmin"]));
        var handler = new JwtSecurityTokenHandler();
        var platformToken = handler.ReadJwtToken(platform.AccessToken);
        Assert.DoesNotContain(platformToken.Claims, c => c.Type == HttpCurrentUser.TenantIdClaim);
        Assert.Contains(platformToken.Claims, c => c.Type == ClaimTypes.Role && c.Value == "PlatformSuperAdmin");

        var tenantId = Guid.CreateVersion7();
        var owner = issuer.Issue(new TokenIssueRequest(
            Guid.CreateVersion7(),
            "owner@example.com",
            tenantId,
            "Owner",
            ["FranchiseeOwner"]));
        var ownerToken = handler.ReadJwtToken(owner.AccessToken);
        Assert.Contains(ownerToken.Claims, c => c.Type == HttpCurrentUser.TenantIdClaim && c.Value == tenantId.ToString());
        Assert.Equal(FixedClock.Now.AddMinutes(30), owner.ExpiresAt);
    }
}
