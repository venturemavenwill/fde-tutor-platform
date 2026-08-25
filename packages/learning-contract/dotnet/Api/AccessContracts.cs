namespace FdeTutor.Contracts.Api;

public sealed record CurrentUserAccessResponse(
    Guid TenantId,
    Guid ObjectId,
    string ExternalSubject,
    string AuthenticationMode,
    bool IsSynthetic,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> EffectiveCapabilities);

public sealed record UserManagementBoundaryResponse(
    string AssignmentAuthority,
    bool RoleMutationAvailable,
    bool EnrolmentAvailable,
    string DirectoryMode);

public sealed record AuthorizationRoleResponse(
    string Id,
    string Label,
    string Description);

public sealed record AuthorizationCapabilityResponse(
    string Id,
    string Label,
    string Constraint,
    IReadOnlyDictionary<string, string> Access);

public sealed record AccessConsoleResponse(
    string SchemaVersion,
    string MatrixVersion,
    CurrentUserAccessResponse CurrentUser,
    UserManagementBoundaryResponse UserManagement,
    IReadOnlyList<AuthorizationRoleResponse> Roles,
    IReadOnlyList<AuthorizationCapabilityResponse> Capabilities);

public sealed record ObservedUserResponse(
    Guid TenantId,
    Guid ObjectId,
    string ExternalSubject,
    string AuthenticationMode,
    IReadOnlyList<string> Roles,
    DateTimeOffset FirstObservedAt,
    DateTimeOffset LastObservedAt);
