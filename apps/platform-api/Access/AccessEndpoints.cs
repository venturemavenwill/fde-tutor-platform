using FdeTutor.Api.Authentication;
using FdeTutor.Contracts.Api;
using FdeTutor.Domain.Authorization;

namespace FdeTutor.Api.Access;

public static class AccessEndpoints
{
    public static IEndpointRouteBuilder MapAccessEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/v1/access")
            .WithTags("Identity and access");

        group.MapGet("", async (
            HttpContext context,
            LearnerAuthorizationContextFactory authorizationFactory,
            IPlatformUserDirectory directory,
            IConfiguration configuration,
            CancellationToken cancellationToken) =>
        {
            var authorization = authorizationFactory.Create(context.User);
            var authenticationMode = RequiredConfiguration(
                configuration,
                "Authentication:Mode");
            await directory.ObserveAsync(
                authorization,
                authenticationMode,
                DateTimeOffset.UtcNow,
                cancellationToken);
            return Results.Ok(CreateConsole(
                authorization,
                authenticationMode,
                RequiredConfiguration(configuration, "Persistence:Provider")));
        }).RequireAuthorization(PlatformPolicies.AuthenticatedAccess);

        group.MapGet("/users", async (
            HttpContext context,
            LearnerAuthorizationContextFactory authorizationFactory,
            IPlatformUserDirectory directory,
            IConfiguration configuration,
            CancellationToken cancellationToken) =>
        {
            var authorization = authorizationFactory.Create(context.User);
            var authenticationMode = RequiredConfiguration(
                configuration,
                "Authentication:Mode");
            await directory.ObserveAsync(
                authorization,
                authenticationMode,
                DateTimeOffset.UtcNow,
                cancellationToken);
            var users = await directory.ListAsync(
                authorization.TenantId,
                cancellationToken);
            return Results.Ok(users.Select(user => new ObservedUserResponse(
                user.TenantId,
                user.ObjectId,
                user.ExternalSubject,
                user.AuthenticationMode,
                user.Roles,
                user.FirstObservedAt,
                user.LastObservedAt)));
        }).RequireAuthorization(PlatformPolicies.AdministratorAccess);

        return endpoints;
    }

    private static AccessConsoleResponse CreateConsole(
        LearnerAuthorizationContext authorization,
        string authenticationMode,
        string persistenceProvider)
    {
        var roles = authorization.Roles
            .Where(PlatformRoles.IsKnown)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return new AccessConsoleResponse(
            "1.0.0",
            Phase1AuthorizationMatrix.Version,
            new CurrentUserAccessResponse(
                authorization.TenantId,
                authorization.LearnerId,
                authorization.ExternalSubject,
                authenticationMode,
                string.Equals(
                    authenticationMode,
                    "Development",
                    StringComparison.Ordinal),
                roles,
                Phase1AuthorizationMatrix.EffectiveCapabilities(
                    roles.ToHashSet(StringComparer.Ordinal))),
            new UserManagementBoundaryResponse(
                string.Equals(
                    authenticationMode,
                    "Entra",
                    StringComparison.Ordinal)
                    ? "Microsoft Entra enterprise application app roles"
                    : "Development-only synthetic role headers",
                RoleMutationAvailable: false,
                EnrolmentAvailable: false,
                DirectoryMode: persistenceProvider),
            Phase1AuthorizationMatrix.Roles
                .Select(role => new AuthorizationRoleResponse(
                    role.Id,
                    role.Label,
                    role.Description))
                .ToArray(),
            Phase1AuthorizationMatrix.Capabilities
                .Select(capability => new AuthorizationCapabilityResponse(
                    capability.Id,
                    capability.Label,
                    capability.Constraint,
                    capability.Access.ToDictionary(
                        item => item.Key,
                        item => item.Value.ToString(),
                        StringComparer.Ordinal)))
                .ToArray());
    }

    private static string RequiredConfiguration(
        IConfiguration configuration,
        string key) =>
        configuration[key] ??
        throw new InvalidOperationException($"{key} is required.");
}
