namespace FdeTutor.Persistence.Entities;

public sealed class LearnerEventEntity
{
    public long RecordedSequence { get; init; }

    public Guid EventId { get; init; }

    public required string EventType { get; init; }

    public int EventVersion { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    public DateTimeOffset RecordedAt { get; init; }

    public Guid TenantId { get; init; }

    public Guid LearnerId { get; init; }

    public Guid SessionId { get; init; }

    public long StreamVersion { get; init; }

    public required string ContentNodeId { get; init; }

    public required string ContentRevision { get; init; }

    public Guid CorrelationId { get; init; }

    public Guid? CausationId { get; init; }

    public required string IdempotencyKey { get; init; }

    public required string ActorType { get; init; }

    public required string ActorId { get; init; }

    public required string PayloadJson { get; init; }
}
