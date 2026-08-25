namespace FdeTutor.Domain.Authorization;

public interface IPlatformUserDirectory
{
    Task ObserveAsync(
        LearnerAuthorizationContext authorization,
        string authenticationMode,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ObservedPlatformUser>> ListAsync(
        Guid tenantId,
        CancellationToken cancellationToken);
}
