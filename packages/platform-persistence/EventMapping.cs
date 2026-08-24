using System.Text.Json;
using FdeTutor.Contracts.Events;
using FdeTutor.Persistence.Entities;

namespace FdeTutor.Persistence;

internal static class EventMapping
{
    public static LearnerEventEnvelope ToContract(this LearnerEventEntity entity)
    {
        using var document = JsonDocument.Parse(entity.PayloadJson);
        return new LearnerEventEnvelope(
            entity.EventId,
            entity.EventType,
            entity.EventVersion,
            entity.OccurredAt,
            entity.RecordedAt,
            entity.TenantId,
            entity.LearnerId,
            entity.SessionId,
            entity.ContentNodeId,
            entity.ContentRevision,
            entity.CorrelationId,
            entity.CausationId,
            entity.IdempotencyKey,
            new EventActor(entity.ActorType, entity.ActorId),
            document.RootElement.Clone());
    }
}
