using System.Text.Json;

namespace FdeTutor.Contracts.Events;

public sealed record EventActor(string Type, string Id);

public sealed record LearnerEventEnvelope(
    Guid EventId,
    string EventType,
    int EventVersion,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt,
    Guid TenantId,
    Guid LearnerId,
    Guid SessionId,
    string ContentNodeId,
    string ContentRevision,
    Guid CorrelationId,
    Guid? CausationId,
    string IdempotencyKey,
    EventActor Actor,
    JsonElement Payload);

public sealed record AppendEventResult(LearnerEventEnvelope Event, bool IsDuplicate);
