namespace FdeTutor.Persistence.Entities;

public sealed class DueRetrievalEntity
{
    public Guid TenantId { get; init; }

    public Guid LearnerId { get; init; }

    public Guid SessionId { get; init; }

    public required string ContentNodeId { get; init; }

    public Guid SourceEventId { get; init; }

    public DateTimeOffset DueAt { get; init; }

    public Guid? CompletedEventId { get; set; }
}
