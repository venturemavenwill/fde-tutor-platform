using System.Text.Json;
using FdeTutor.Domain.Authorization;

namespace FdeTutor.Domain.Events;

public sealed record AppendLearnerEventCommand(
    LearnerAuthorizationContext Authorization,
    Guid SessionId,
    string EventType,
    string ContentNodeId,
    string ContentRevision,
    long ExpectedStreamVersion,
    Guid CorrelationId,
    Guid? CausationId,
    string IdempotencyKey,
    DateTimeOffset OccurredAt,
    JsonElement Payload);
