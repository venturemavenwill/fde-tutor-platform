using FdeTutor.Domain.Authorization;
using FdeTutor.Persistence;

namespace FdeTutor.Persistence.Tests;

public sealed class PlatformUserDirectoryTests
{
    [Fact]
    public async Task DirectoryKeepsOneDurableSubjectAndLatestObservedRoles()
    {
        var directory = new InMemoryPlatformUserDirectory();
        var tenantId = Guid.NewGuid();
        var objectId = Guid.NewGuid();
        var first = new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero);
        var second = first.AddHours(1);

        await directory.ObserveAsync(
            Authorization(tenantId, objectId, PlatformRoles.Learner),
            "Entra",
            first,
            CancellationToken.None);
        await directory.ObserveAsync(
            Authorization(
                tenantId,
                objectId,
                PlatformRoles.Learner,
                PlatformRoles.Administrator),
            "Entra",
            second,
            CancellationToken.None);

        var user = Assert.Single(await directory.ListAsync(
            tenantId,
            CancellationToken.None));
        Assert.Equal(first, user.FirstObservedAt);
        Assert.Equal(second, user.LastObservedAt);
        Assert.Equal(
            [PlatformRoles.Administrator, PlatformRoles.Learner],
            user.Roles);
    }

    [Fact]
    public async Task DirectoryListingCannotCrossTenantBoundary()
    {
        var directory = new InMemoryPlatformUserDirectory();
        var firstTenant = Guid.NewGuid();
        var secondTenant = Guid.NewGuid();
        await directory.ObserveAsync(
            Authorization(firstTenant, Guid.NewGuid(), PlatformRoles.Administrator),
            "Entra",
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        var users = await directory.ListAsync(
            secondTenant,
            CancellationToken.None);

        Assert.Empty(users);
    }

    private static LearnerAuthorizationContext Authorization(
        Guid tenantId,
        Guid objectId,
        params string[] roles) =>
        new(
            tenantId,
            objectId,
            $"{tenantId:D}:{objectId:D}",
            roles.ToHashSet(StringComparer.Ordinal));
}
