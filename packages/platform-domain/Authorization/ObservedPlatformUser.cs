namespace FdeTutor.Domain.Authorization;

public sealed record ObservedPlatformUser(
    Guid TenantId,
    Guid ObjectId,
    string ExternalSubject,
    string AuthenticationMode,
    IReadOnlyList<string> Roles,
    DateTimeOffset FirstObservedAt,
    DateTimeOffset LastObservedAt);
