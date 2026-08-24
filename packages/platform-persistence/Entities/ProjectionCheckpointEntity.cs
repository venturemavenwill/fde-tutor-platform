namespace FdeTutor.Persistence.Entities;

public sealed class ProjectionCheckpointEntity
{
    public required string ProjectionName { get; init; }

    public required string PartitionKey { get; init; }

    public DateTimeOffset? LastRecordedAt { get; set; }

    public Guid? LastEventId { get; set; }

    public Guid? FailureEventId { get; set; }

    public int FailureCount { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
