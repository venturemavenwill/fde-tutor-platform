namespace FdeTutor.Persistence.Entities;

public sealed class OutboxMessageEntity
{
    public Guid MessageId { get; init; }

    public Guid TenantId { get; init; }

    public Guid EventId { get; init; }

    public required string Topic { get; init; }

    public required string PayloadJson { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset AvailableAt { get; set; }

    public DateTimeOffset? ClaimedAt { get; set; }

    public string? ClaimOwner { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    public int AttemptCount { get; set; }

    public string? LastError { get; set; }
}
