using System.Security.Claims;
using FdeTutor.Domain.Authorization;

namespace FdeTutor.Api.Authentication;

public sealed class LearnerAuthorizationContextFactory(IConfiguration configuration)
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

    public LearnerAuthorizationContext Create(ClaimsPrincipal principal)
    {
        var tenantValue = FindClaim(principal, TenantClaimTypes);
        var objectValue = FindClaim(principal, ObjectClaimTypes);

        if (!Guid.TryParse(tenantValue, out var tenantId) ||
            !Guid.TryParse(objectValue, out var objectId))
        {
            throw new UnauthorizedAccessException(
                "The validated identity does not contain durable tid and oid claims.");
        }

        var allowedTenantValue = configuration["Authentication:AllowedTenantId"];
        if (!Guid.TryParse(allowedTenantValue, out var allowedTenantId))
        {
            throw new InvalidOperationException(
                "Authentication:AllowedTenantId must be a UUID.");
        }

        if (tenantId != allowedTenantId)
        {
            throw new UnauthorizedAccessException("The token tenant is not approved.");
        }

        var roles = principal
            .FindAll(ClaimTypes.Role)
            .Select(claim => claim.Value)
            .ToHashSet(StringComparer.Ordinal);

        return new LearnerAuthorizationContext(
            tenantId,
            objectId,
            $"{tenantId:D}:{objectId:D}",
            roles);
    }

    private static string? FindClaim(
        ClaimsPrincipal principal,
        IEnumerable<string> claimTypes) =>
        claimTypes
            .Select(principal.FindFirstValue)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
