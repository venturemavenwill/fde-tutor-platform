using FdeTutor.Persistence;
using FdeTutor.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace FdeTutor.Persistence.Tests;

public sealed class PersistenceModelTests
{
    [Fact]
    public void EventIdempotencyIsTenantScopedAndUnique()
    {
        using var context = CreateContext();
        var eventType = context.Model.FindEntityType(typeof(LearnerEventEntity));
        var index = eventType?.GetIndexes().SingleOrDefault(candidate =>
            candidate.Properties.Select(property => property.Name)
                .SequenceEqual(
                    [nameof(LearnerEventEntity.TenantId), nameof(LearnerEventEntity.IdempotencyKey)]));

        Assert.NotNull(index);
        Assert.True(index.IsUnique);
    }

    [Fact]
    public void S083ProgressHasNoMasteryOrEntrustmentProperty()
    {
        using var context = CreateContext();
        var progressType = context.Model.FindEntityType(typeof(S083ProgressEntity));
        var propertyNames = progressType?.GetProperties().Select(property => property.Name).ToArray();

        Assert.NotNull(propertyNames);
        Assert.DoesNotContain(propertyNames, name =>
            name.Contains("Mastery", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Entrustment", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PlatformUserDirectoryUsesTenantAndObjectCompositeIdentity()
    {
        using var context = CreateContext();
        var userType = context.Model.FindEntityType(typeof(PlatformUserEntity));
        var key = userType?.FindPrimaryKey();
        var properties = userType?.GetProperties().Select(property => property.Name).ToArray();

        Assert.Equal(
            [nameof(PlatformUserEntity.TenantId), nameof(PlatformUserEntity.ObjectId)],
            key?.Properties.Select(property => property.Name));
        Assert.DoesNotContain(
            properties!,
            property => property.Contains("Email", StringComparison.OrdinalIgnoreCase));
    }

    private static FdeTutorDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<FdeTutorDbContext>()
            .UseNpgsql("Host=localhost;Database=fde_tutor_model_test;Username=test;Password=test")
            .Options;
        return new FdeTutorDbContext(options);
    }
}
