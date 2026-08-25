using FdeTutor.Domain.Authorization;

namespace FdeTutor.Api.Authentication;

public sealed class LearnerAuthorizationContextFactory(IConfiguration configuration)
{
    public LearnerAuthorizationContext Create(
        System.Security.Claims.ClaimsPrincipal principal)
    {
        if (!PlatformClaims.TryGetSubject(principal, out var tenantId, out var objectId))
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

        return new LearnerAuthorizationContext(
            tenantId,
            objectId,
            $"{tenantId:D}:{objectId:D}",
            PlatformClaims.GetKnownRoles(principal));
    }
}
