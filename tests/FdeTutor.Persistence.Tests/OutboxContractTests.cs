using System.Text.Json;
using FdeTutor.Contracts.Events;
using FdeTutor.Contracts.Serialization;

namespace FdeTutor.Persistence.Tests;

public sealed class OutboxContractTests
{
    [Fact]
    public void EventEnvelopeUsesTheCanonicalCamelCaseWireContract()
    {
        var envelope = new LearnerEventEnvelope(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            LearnerEventTypes.LearningSessionStarted,
            1,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            "S083",
            "032601ea05b48ed716e72ac217a0024ec6ae413b0b27113c704ba6ab4f332522",
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            null,
            "idempotency-key",
            new EventActor("learner", "subject"),
            JsonSerializer.SerializeToElement(new { route = "S083" }));

        using var document = JsonDocument.Parse(ContractJson.Serialize(envelope));
        var root = document.RootElement;

        Assert.True(root.TryGetProperty("eventId", out _));
        Assert.True(root.TryGetProperty("eventType", out _));
        Assert.True(root.TryGetProperty("contentRevision", out _));
        Assert.False(root.TryGetProperty("EventId", out _));
        Assert.False(root.TryGetProperty("EventType", out _));
    }
}
