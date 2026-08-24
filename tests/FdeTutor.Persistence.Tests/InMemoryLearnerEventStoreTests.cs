using System.Text.Json;
using FdeTutor.Contracts.Events;
using FdeTutor.Domain.Authorization;
using FdeTutor.Domain.Events;
using FdeTutor.Persistence;

namespace FdeTutor.Persistence.Tests;

public sealed class InMemoryLearnerEventStoreTests
{
    [Fact]
    public async Task RepeatedIdempotencyKeyReturnsOriginalEvent()
    {
        var store = new InMemoryLearnerEventStore();
        var authorization = Authorization(
            "11111111-1111-1111-1111-111111111111",
            "22222222-2222-2222-2222-222222222222");
        var command = Command(authorization, "idempotency-key-1");

        var first = await store.AppendAsync(command, CancellationToken.None);
        var second = await store.AppendAsync(command, CancellationToken.None);
        var timeline = await store.ReadSessionAsync(
            authorization,
            command.SessionId,
            CancellationToken.None);

        Assert.False(first.IsDuplicate);
        Assert.True(second.IsDuplicate);
        Assert.Equal(first.Event.EventId, second.Event.EventId);
        Assert.Single(timeline);
    }

    [Fact]
    public async Task TenantAndLearnerPredicatesPreventObjectLeakage()
    {
        var store = new InMemoryLearnerEventStore();
        var owner = Authorization(
            "11111111-1111-1111-1111-111111111111",
            "22222222-2222-2222-2222-222222222222");
        var otherTenant = Authorization(
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            "22222222-2222-2222-2222-222222222222");
        var otherLearner = Authorization(
            "11111111-1111-1111-1111-111111111111",
            "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var command = Command(owner, "idempotency-key-2");
        await store.AppendAsync(command, CancellationToken.None);

        var crossTenant = await store.ReadSessionAsync(
            otherTenant,
            command.SessionId,
            CancellationToken.None);
        var crossLearner = await store.ReadSessionAsync(
            otherLearner,
            command.SessionId,
            CancellationToken.None);

        Assert.Empty(crossTenant);
        Assert.Empty(crossLearner);
    }

    [Fact]
    public async Task StaleExpectedStreamVersionCannotAppendASecondTransition()
    {
        var store = new InMemoryLearnerEventStore();
        var authorization = Authorization(
            "11111111-1111-1111-1111-111111111111",
            "22222222-2222-2222-2222-222222222222");
        var start = await store.AppendAsync(
            Command(authorization, "idempotency-key-start"),
            CancellationToken.None);
        var first = Transition(
            authorization,
            start.Event.SessionId,
            "idempotency-key-expectation-1",
            start.Event.EventId);
        var stale = Transition(
            authorization,
            start.Event.SessionId,
            "idempotency-key-expectation-2",
            start.Event.EventId);

        await store.AppendAsync(first, CancellationToken.None);

        await Assert.ThrowsAsync<StreamConcurrencyException>(
            () => store.AppendAsync(stale, CancellationToken.None));
    }

    private static LearnerAuthorizationContext Authorization(
        string tenantId,
        string learnerId) =>
        new(
            Guid.Parse(tenantId),
            Guid.Parse(learnerId),
            $"{tenantId}:{learnerId}",
            new HashSet<string>(StringComparer.Ordinal) { "Learner" });

    private static AppendLearnerEventCommand Command(
        LearnerAuthorizationContext authorization,
        string idempotencyKey) =>
        new(
            authorization,
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            LearnerEventTypes.LearningSessionStarted,
            "S083",
            "032601ea05b48ed716e72ac217a0024ec6ae413b0b27113c704ba6ab4f332522",
            0,
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            null,
            idempotencyKey,
            DateTimeOffset.UtcNow,
            JsonSerializer.SerializeToElement(new { route = "S083" }));

    private static AppendLearnerEventCommand Transition(
        LearnerAuthorizationContext authorization,
        Guid sessionId,
        string idempotencyKey,
        Guid causationId) =>
        new(
            authorization,
            sessionId,
            LearnerEventTypes.ExpectationRecorded,
            "S083",
            "032601ea05b48ed716e72ac217a0024ec6ae413b0b27113c704ba6ab4f332522",
            1,
            Guid.NewGuid(),
            causationId,
            idempotencyKey,
            DateTimeOffset.UtcNow,
            JsonSerializer.SerializeToElement(new { response = "Expected outcome" }));
}
