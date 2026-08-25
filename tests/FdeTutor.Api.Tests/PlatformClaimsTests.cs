using System.Security.Claims;
using FdeTutor.Api.Authentication;
using FdeTutor.Domain.Authorization;

namespace FdeTutor.Api.Tests;

public sealed class PlatformClaimsTests
{
    [Fact]
    public void EntraRolesClaimUsesTheSameAllowListAsDevelopmentRoles()
    {
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
            [
                new Claim("roles", PlatformRoles.Learner),
                new Claim("roles", PlatformRoles.Administrator),
                new Claim("roles", "UnrelatedApplicationRole"),
            ],
            "Bearer"));

        var roles = PlatformClaims.GetKnownRoles(principal);

        Assert.Equal(2, roles.Count);
        Assert.Contains(PlatformRoles.Learner, roles);
        Assert.Contains(PlatformRoles.Administrator, roles);
        Assert.DoesNotContain("UnrelatedApplicationRole", roles);
    }
}
