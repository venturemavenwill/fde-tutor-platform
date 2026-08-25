using System.Security.Claims;
using FdeTutor.Domain.Authorization;

namespace FdeTutor.Api.Authentication;

public static class PlatformClaims
{
    private static readonly string[] TenantClaimTypes =
    [
        "tid",
        "http://schemas.microsoft.com/identity/claims/tenantid",
    ];

    private static readonly string[] ObjectClaimTypes =
    [
        "oid",
        "http://schemas.microsoft.com/identity/claims/objectidentifier",
        ClaimTypes.NameIdentifier,
    ];

    private static readonly string[] ScopeClaimTypes =
    [
        "scp",
        "scope",
        "http://schemas.microsoft.com/identity/claims/scope",
    ];

    public static bool TryGetSubject(
        ClaimsPrincipal principal,
        out Guid tenantId,
        out Guid objectId)
    {
        var tenantValue = FindClaim(principal, TenantClaimTypes);
        var objectValue = FindClaim(principal, ObjectClaimTypes);
        var hasTenant = Guid.TryParse(tenantValue, out tenantId);
        var hasObject = Guid.TryParse(objectValue, out objectId);
        return hasTenant && hasObject;
    }

    public static bool HasApprovedSubject(
        ClaimsPrincipal principal,
        Guid allowedTenantId) =>
        TryGetSubject(principal, out var tenantId, out _) &&
        tenantId == allowedTenantId;

    public static bool HasDelegatedScope(
        ClaimsPrincipal principal,
        string requiredScope) =>
        ScopeClaimTypes
            .SelectMany(principal.FindAll)
            .SelectMany(claim => claim.Value.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries))
            .Contains(requiredScope, StringComparer.Ordinal);

    public static IReadOnlySet<string> GetKnownRoles(ClaimsPrincipal principal) =>
        principal
            .FindAll(ClaimTypes.Role)
            .Concat(principal.FindAll("roles"))
            .Select(claim => claim.Value)
            .Where(PlatformRoles.IsKnown)
            .ToHashSet(StringComparer.Ordinal);

    private static string? FindClaim(
        ClaimsPrincipal principal,
        IEnumerable<string> claimTypes) =>
        claimTypes
            .Select(principal.FindFirstValue)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
