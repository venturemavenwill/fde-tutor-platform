using System.Security.Claims;
using System.Text.Encodings.Web;
using FdeTutor.Domain.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace FdeTutor.Api.Authentication;

public sealed class DevelopmentAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Development";
    public const string TenantHeader = "X-Fde-Tenant-Id";
    public const string ObjectHeader = "X-Fde-Object-Id";
    public const string RolesHeader = "X-Fde-Roles";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(TenantHeader, out var tenantHeader) ||
            !Guid.TryParse(tenantHeader, out var tenantId))
        {
            return Task.FromResult(AuthenticateResult.Fail(
                $"A valid {TenantHeader} header is required in development mode."));
        }

        if (!Request.Headers.TryGetValue(ObjectHeader, out var objectHeader) ||
            !Guid.TryParse(objectHeader, out var objectId))
        {
            return Task.FromResult(AuthenticateResult.Fail(
                $"A valid {ObjectHeader} header is required in development mode."));
        }

        var roles = new[] { PlatformRoles.Learner };
        if (Request.Headers.TryGetValue(RolesHeader, out var rolesHeader))
        {
            roles = rolesHeader
                .ToString()
                .Split(
                    ',',
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (roles.Length == 0 || roles.Any(role => !PlatformRoles.IsKnown(role)))
            {
                return Task.FromResult(AuthenticateResult.Fail(
                    $"{RolesHeader} contains an unknown platform role."));
            }
        }

        var claims = new List<Claim>
        {
            new Claim("tid", tenantId.ToString()),
            new Claim("oid", objectId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, objectId.ToString()),
            new Claim("scp", "access_as_user"),
            new Claim(
                "http://schemas.microsoft.com/identity/claims/scope",
                "access_as_user"),
        };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
