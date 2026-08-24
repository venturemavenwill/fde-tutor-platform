using FdeTutor.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace FdeTutor.Persistence;

public sealed class FdeTutorDbContext(DbContextOptions<FdeTutorDbContext> options)
    : DbContext(options)
{
    public DbSet<LearnerEventEntity> LearnerEvents => Set<LearnerEventEntity>();

    public DbSet<OutboxMessageEntity> OutboxMessages => Set<OutboxMessageEntity>();

    public DbSet<ProcessedProjectionEventEntity> ProcessedProjectionEvents =>
        Set<ProcessedProjectionEventEntity>();

    public DbSet<ProjectionCheckpointEntity> ProjectionCheckpoints =>
        Set<ProjectionCheckpointEntity>();

    public DbSet<S083ProgressEntity> S083Progress => Set<S083ProgressEntity>();

    public DbSet<DueRetrievalEntity> DueRetrievals => Set<DueRetrievalEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var events = modelBuilder.Entity<LearnerEventEntity>();
        events.ToTable("learner_events");
        events.HasKey(item => item.EventId);
        events.Property(item => item.RecordedSequence)
            .HasColumnName("recorded_sequence")
            .ValueGeneratedOnAdd();
        events.HasIndex(item => item.RecordedSequence).IsUnique();
        events.Property(item => item.EventId).HasColumnName("event_id");
        events.Property(item => item.EventType).HasColumnName("event_type").HasMaxLength(100);
        events.Property(item => item.EventVersion).HasColumnName("event_version");
        events.Property(item => item.OccurredAt).HasColumnName("occurred_at");
        events.Property(item => item.RecordedAt).HasColumnName("recorded_at");
        events.Property(item => item.TenantId).HasColumnName("tenant_id");
        events.Property(item => item.LearnerId).HasColumnName("learner_id");
        events.Property(item => item.SessionId).HasColumnName("session_id");
        events.Property(item => item.StreamVersion).HasColumnName("stream_version");
        events.Property(item => item.ContentNodeId).HasColumnName("content_node_id").HasMaxLength(20);
        events.Property(item => item.ContentRevision).HasColumnName("content_revision").HasMaxLength(64);
        events.Property(item => item.CorrelationId).HasColumnName("correlation_id");
        events.Property(item => item.CausationId).HasColumnName("causation_id");
        events.Property(item => item.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(128);
        events.Property(item => item.ActorType).HasColumnName("actor_type").HasMaxLength(32);
        events.Property(item => item.ActorId).HasColumnName("actor_id").HasMaxLength(256);
        events.Property(item => item.PayloadJson).HasColumnName("payload").HasColumnType("jsonb");
        events.HasIndex(item => new { item.TenantId, item.IdempotencyKey }).IsUnique();
        events.HasIndex(item => new
        {
            item.TenantId,
            item.LearnerId,
            item.SessionId,
            item.RecordedAt,
            item.EventId,
        });
        events.HasIndex(item => new
        {
            item.TenantId,
            item.LearnerId,
            item.SessionId,
            item.StreamVersion,
        }).IsUnique();

        var outbox = modelBuilder.Entity<OutboxMessageEntity>();
        outbox.ToTable("outbox_messages");
        outbox.HasKey(item => item.MessageId);
        outbox.Property(item => item.MessageId).HasColumnName("message_id");
        outbox.Property(item => item.TenantId).HasColumnName("tenant_id");
        outbox.Property(item => item.EventId).HasColumnName("event_id");
        outbox.Property(item => item.Topic).HasColumnName("topic").HasMaxLength(200);
        outbox.Property(item => item.PayloadJson).HasColumnName("payload").HasColumnType("jsonb");
        outbox.Property(item => item.CreatedAt).HasColumnName("created_at");
        outbox.Property(item => item.AvailableAt).HasColumnName("available_at");
        outbox.Property(item => item.ClaimedAt).HasColumnName("claimed_at");
        outbox.Property(item => item.ClaimOwner).HasColumnName("claim_owner").HasMaxLength(128);
        outbox.Property(item => item.PublishedAt).HasColumnName("published_at");
        outbox.Property(item => item.AttemptCount).HasColumnName("attempt_count");
        outbox.Property(item => item.LastError).HasColumnName("last_error").HasMaxLength(2000);
        outbox.HasIndex(item => item.EventId).IsUnique();
        outbox.HasIndex(item => new { item.PublishedAt, item.AvailableAt });

        outbox
            .HasOne<LearnerEventEntity>()
            .WithOne()
            .HasForeignKey<OutboxMessageEntity>(item => item.EventId)
            .OnDelete(DeleteBehavior.Restrict);

        var processed = modelBuilder.Entity<ProcessedProjectionEventEntity>();
        processed.ToTable("processed_projection_events");
        processed.HasKey(item => new { item.ProjectionName, item.EventId });
        processed.Property(item => item.ProjectionName)
            .HasColumnName("projection_name")
            .HasMaxLength(100);
        processed.Property(item => item.EventId).HasColumnName("event_id");
        processed.Property(item => item.ProcessedAt).HasColumnName("processed_at");

        var checkpoint = modelBuilder.Entity<ProjectionCheckpointEntity>();
        checkpoint.ToTable("projection_checkpoints");
        checkpoint.HasKey(item => new { item.ProjectionName, item.PartitionKey });
        checkpoint.Property(item => item.ProjectionName)
            .HasColumnName("projection_name")
            .HasMaxLength(100);
        checkpoint.Property(item => item.PartitionKey)
            .HasColumnName("partition_key")
            .HasMaxLength(200);
        checkpoint.Property(item => item.LastRecordedAt).HasColumnName("last_recorded_at");
        checkpoint.Property(item => item.LastEventId).HasColumnName("last_event_id");
        checkpoint.Property(item => item.FailureEventId).HasColumnName("failure_event_id");
        checkpoint.Property(item => item.FailureCount).HasColumnName("failure_count");
        checkpoint.Property(item => item.LastError)
            .HasColumnName("last_error")
            .HasMaxLength(2000);
        checkpoint.Property(item => item.UpdatedAt).HasColumnName("updated_at");

        var progress = modelBuilder.Entity<S083ProgressEntity>();
        progress.ToTable("s083_progress");
        progress.HasKey(item => new { item.TenantId, item.LearnerId, item.SessionId });
        progress.Property(item => item.TenantId).HasColumnName("tenant_id");
        progress.Property(item => item.LearnerId).HasColumnName("learner_id");
        progress.Property(item => item.SessionId).HasColumnName("session_id");
        progress.Property(item => item.ContentRevision)
            .HasColumnName("content_revision")
            .HasMaxLength(64);
        progress.Property(item => item.State).HasColumnName("state").HasMaxLength(64);
        progress.Property(item => item.CriterionRevealAllowed)
            .HasColumnName("criterion_reveal_allowed");
        progress.Property(item => item.PaidProposalImprovementAllowed)
            .HasColumnName("paid_proposal_improvement_allowed");
        progress.Property(item => item.SupportUsedJson)
            .HasColumnName("support_used")
            .HasColumnType("jsonb");
        progress.Property(item => item.ProjectionVersion).HasColumnName("projection_version");
        progress.Property(item => item.LastEventId).HasColumnName("last_event_id");
        progress.Property(item => item.UpdatedAt).HasColumnName("updated_at");

        var due = modelBuilder.Entity<DueRetrievalEntity>();
        due.ToTable("due_retrievals");
        due.HasKey(item => new
        {
            item.TenantId,
            item.LearnerId,
            item.SessionId,
            item.SourceEventId,
        });
        due.Property(item => item.TenantId).HasColumnName("tenant_id");
        due.Property(item => item.LearnerId).HasColumnName("learner_id");
        due.Property(item => item.SessionId).HasColumnName("session_id");
        due.Property(item => item.ContentNodeId).HasColumnName("content_node_id").HasMaxLength(20);
        due.Property(item => item.SourceEventId).HasColumnName("source_event_id");
        due.Property(item => item.DueAt).HasColumnName("due_at");
        due.Property(item => item.CompletedEventId).HasColumnName("completed_event_id");
    }
}
