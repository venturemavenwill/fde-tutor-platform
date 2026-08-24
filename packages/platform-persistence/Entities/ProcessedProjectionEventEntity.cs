namespace FdeTutor.Persistence.Entities;

public sealed class ProcessedProjectionEventEntity
{
    public required string ProjectionName { get; init; }

    public Guid EventId { get; init; }

    public DateTimeOffset ProcessedAt { get; init; }
}
