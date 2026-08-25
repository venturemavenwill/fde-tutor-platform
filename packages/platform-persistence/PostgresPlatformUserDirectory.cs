using System.Text.Json;
using FdeTutor.Domain.Authorization;
using Microsoft.EntityFrameworkCore;

namespace FdeTutor.Persistence;

public sealed class PostgresPlatformUserDirectory(FdeTutorDbContext dbContext)
    : IPlatformUserDirectory
{
    public async Task ObserveAsync(
        LearnerAuthorizationContext authorization,
        string authenticationMode,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        var rolesJson = JsonSerializer.Serialize(
            authorization.Roles
                .Where(PlatformRoles.IsKnown)
                .Order(StringComparer.Ordinal));
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO platform_users (
                 tenant_id,
                 object_id,
                 external_subject,
                 authentication_mode,
                 roles,
                 first_observed_at,
                 last_observed_at)
             VALUES (
                 {authorization.TenantId},
                 {authorization.LearnerId},
                 {authorization.ExternalSubject},
                 {authenticationMode},
                 CAST({rolesJson} AS jsonb),
                 {observedAt},
                 {observedAt})
             ON CONFLICT (tenant_id, object_id)
             DO UPDATE SET
                 external_subject = EXCLUDED.external_subject,
                 authentication_mode = EXCLUDED.authentication_mode,
                 roles = EXCLUDED.roles,
                 last_observed_at = EXCLUDED.last_observed_at
             """,
            cancellationToken);
    }

    public async Task<IReadOnlyList<ObservedPlatformUser>> ListAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var entities = await dbContext.PlatformUsers
            .AsNoTracking()
            .Where(user => user.TenantId == tenantId)
            .OrderBy(user => user.ObjectId)
            .ToArrayAsync(cancellationToken);
        return entities
            .Select(user => new ObservedPlatformUser(
                user.TenantId,
                user.ObjectId,
                user.ExternalSubject,
                user.AuthenticationMode,
                JsonSerializer.Deserialize<string[]>(user.RolesJson) ??
                    Array.Empty<string>(),
                user.FirstObservedAt,
                user.LastObservedAt))
            .ToArray();
    }
}
