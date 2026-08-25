using System.Collections.Concurrent;
using FdeTutor.Domain.Authorization;

namespace FdeTutor.Persistence;

public sealed class InMemoryPlatformUserDirectory : IPlatformUserDirectory
{
    private readonly ConcurrentDictionary<
        (Guid TenantId, Guid ObjectId),
        ObservedPlatformUser> users = new();

    public Task ObserveAsync(
        LearnerAuthorizationContext authorization,
        string authenticationMode,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var roles = authorization.Roles
            .Where(PlatformRoles.IsKnown)
            .Order(StringComparer.Ordinal)
            .ToArray();
        users.AddOrUpdate(
            (authorization.TenantId, authorization.LearnerId),
            _ => new ObservedPlatformUser(
                authorization.TenantId,
                authorization.LearnerId,
                authorization.ExternalSubject,
                authenticationMode,
                roles,
                observedAt,
                observedAt),
            (_, existing) => existing with
            {
                ExternalSubject = authorization.ExternalSubject,
                AuthenticationMode = authenticationMode,
                Roles = roles,
                LastObservedAt = observedAt,
            });
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ObservedPlatformUser>> ListAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<ObservedPlatformUser> result = users.Values
            .Where(user => user.TenantId == tenantId)
            .OrderBy(user => user.ObjectId)
            .ToArray();
        return Task.FromResult(result);
    }
}
