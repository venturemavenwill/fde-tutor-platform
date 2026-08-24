namespace FdeTutor.Domain.Authorization;

public sealed record LearnerAuthorizationContext(
    Guid TenantId,
    Guid LearnerId,
    string ExternalSubject,
    IReadOnlySet<string> Roles);
