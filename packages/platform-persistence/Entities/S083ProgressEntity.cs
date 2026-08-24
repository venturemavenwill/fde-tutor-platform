namespace FdeTutor.Persistence.Entities;

public sealed class S083ProgressEntity
{
    public Guid TenantId { get; init; }

    public Guid LearnerId { get; init; }

    public Guid SessionId { get; init; }

    public required string ContentRevision { get; set; }

    public required string State { get; set; }

    public bool CriterionRevealAllowed { get; set; }

    public bool PaidProposalImprovementAllowed { get; set; }

    public required string SupportUsedJson { get; set; }

    public long ProjectionVersion { get; set; }

    public Guid LastEventId { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
