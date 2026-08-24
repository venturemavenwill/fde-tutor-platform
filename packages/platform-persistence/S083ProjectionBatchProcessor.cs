using FdeTutor.Contracts.Events;
using FdeTutor.Contracts.Policy;
using FdeTutor.Contracts.Serialization;
using FdeTutor.Domain.Policy;
using FdeTutor.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace FdeTutor.Persistence;

public sealed class S083ProjectionBatchProcessor(
    FdeTutorDbContext dbContext,
    TimeProvider timeProvider)
{
    public const string ProjectionName = "s083-progress-v1";
    private const string PartitionKey = "global";

    public async Task<int> ProcessBatchAsync(
        string workerId,
        int batchSize,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);

        var now = timeProvider.GetUtcNow();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            cancellationToken);

        // One ordered writer protects per-stream causality while still allowing
        // multiple worker replicas to compete safely.
        await dbContext.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock(hashtext('s083-progress-v1'));",
            cancellationToken);

        var checkpoint = await dbContext.ProjectionCheckpoints.FindAsync(
            [ProjectionName, PartitionKey],
            cancellationToken);
        if (checkpoint is null)
        {
            checkpoint = new ProjectionCheckpointEntity
            {
                ProjectionName = ProjectionName,
                PartitionKey = PartitionKey,
                UpdatedAt = now,
            };
            dbContext.ProjectionCheckpoints.Add(checkpoint);
        }

        var events = await dbContext.LearnerEvents
            .AsNoTracking()
            .Where(item =>
                dbContext.OutboxMessages.Any(message => message.EventId == item.EventId) &&
                !dbContext.ProcessedProjectionEvents.Any(processed =>
                    processed.ProjectionName == ProjectionName &&
                    processed.EventId == item.EventId))
            .OrderBy(item => item.RecordedSequence)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
        var eventIds = events.Select(item => item.EventId).ToArray();
        var messagesByEvent = await dbContext.OutboxMessages
            .Where(message => eventIds.Contains(message.EventId))
            .ToDictionaryAsync(message => message.EventId, cancellationToken);
        events = events
            .TakeWhile(entity => messagesByEvent[entity.EventId].AvailableAt <= now)
            .ToList();

        var processedCount = 0;
        ProjectionPoisonException? poison = null;
        var affectedLearners = new HashSet<(Guid TenantId, Guid LearnerId)>();
        foreach (var entity in events)
        {
            var savepoint = $"event_{processedCount}";
            await transaction.CreateSavepointAsync(savepoint, cancellationToken);
            var message = messagesByEvent[entity.EventId];
            message.ClaimedAt = now;
            message.ClaimOwner = workerId;
            message.AttemptCount += 1;
            try
            {
                var envelope = entity.ToContract();
                await ApplyAsync(
                    envelope,
                    entity.RecordedSequence,
                    now,
                    cancellationToken);
                dbContext.ProcessedProjectionEvents.Add(
                    new ProcessedProjectionEventEntity
                    {
                        ProjectionName = ProjectionName,
                        EventId = envelope.EventId,
                        ProcessedAt = now,
                    });
                checkpoint.LastRecordedAt = envelope.RecordedAt;
                checkpoint.LastEventId = envelope.EventId;
                checkpoint.FailureEventId = null;
                checkpoint.FailureCount = 0;
                checkpoint.LastError = null;
                checkpoint.UpdatedAt = now;
                message.PublishedAt ??= now;
                message.ClaimedAt = null;
                message.ClaimOwner = null;
                message.LastError = null;
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.ReleaseSavepointAsync(savepoint, cancellationToken);
                affectedLearners.Add((entity.TenantId, entity.LearnerId));
                processedCount += 1;
            }
            catch (Exception exception)
                when (exception is System.Text.Json.JsonException or InvalidOperationException)
            {
                await transaction.RollbackToSavepointAsync(savepoint, cancellationToken);
                dbContext.ChangeTracker.Clear();
                checkpoint = await dbContext.ProjectionCheckpoints.FindAsync(
                    [ProjectionName, PartitionKey],
                    cancellationToken)
                    ?? new ProjectionCheckpointEntity
                    {
                        ProjectionName = ProjectionName,
                        PartitionKey = PartitionKey,
                        UpdatedAt = now,
                    };
                if (dbContext.Entry(checkpoint).State == EntityState.Detached)
                {
                    dbContext.ProjectionCheckpoints.Add(checkpoint);
                }

                var failedMessage = await dbContext.OutboxMessages.SingleAsync(
                    item => item.EventId == entity.EventId,
                    cancellationToken);
                checkpoint.FailureEventId = entity.EventId;
                checkpoint.FailureCount += 1;
                checkpoint.LastError = Limit(exception.Message);
                checkpoint.UpdatedAt = now;
                failedMessage.ClaimedAt = null;
                failedMessage.ClaimOwner = null;
                failedMessage.AttemptCount += 1;
                failedMessage.LastError = Limit(exception.Message);
                await dbContext.SaveChangesAsync(cancellationToken);
                poison = new ProjectionPoisonException(
                    entity.EventId,
                    exception.Message);
                break;
            }
        }

        await RefreshEffectiveRightsAsync(
            now,
            affectedLearners,
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        if (poison is not null)
        {
            throw poison;
        }
        return processedCount;
    }

    private async Task ApplyAsync(
        LearnerEventEnvelope currentEvent,
        long currentSequence,
        DateTimeOffset processedAt,
        CancellationToken cancellationToken)
    {
        var entities = await dbContext.LearnerEvents
            .AsNoTracking()
            .Where(item =>
                item.TenantId == currentEvent.TenantId &&
                item.LearnerId == currentEvent.LearnerId &&
                item.SessionId == currentEvent.SessionId &&
                item.RecordedSequence <= currentSequence)
            .OrderBy(item => item.RecordedSequence)
            .ToListAsync(cancellationToken);
        var timeline = entities.Select(item => item.ToContract()).ToArray();
        DateTimeOffset? scheduledDueAt = null;
        if (currentEvent.EventType == LearnerEventTypes.RetrievalScheduled)
        {
            if (!currentEvent.Payload.TryGetProperty("dueAt", out var dueAtProperty) ||
                !DateTimeOffset.TryParse(dueAtProperty.GetString(), out var dueAt))
            {
                throw new InvalidOperationException(
                    "RetrievalScheduled requires a valid dueAt value.");
            }
            scheduledDueAt = dueAt.ToUniversalTime();
        }

        // Projection state is a pure function of recorded events. Whether a
        // scheduled retrieval is due is calculated at query time or represented
        // by a future RetrievalBecameDue event.
        var decision = S083Policy.Evaluate(timeline, currentEvent.RecordedAt);
        var supportUsedJson = ContractJson.Serialize(
            S083Policy.ProjectSupportUsed(timeline));
        var progress = await dbContext.S083Progress.FindAsync(
            [currentEvent.TenantId, currentEvent.LearnerId, currentEvent.SessionId],
            cancellationToken);
        if (progress is null)
        {
            progress = new S083ProgressEntity
            {
                TenantId = currentEvent.TenantId,
                LearnerId = currentEvent.LearnerId,
                SessionId = currentEvent.SessionId,
                ContentRevision = currentEvent.ContentRevision,
                State = decision.State.ToString(),
                CriterionRevealAllowed = decision.CriterionRevealAllowed,
                PaidProposalImprovementAllowed = decision.PaidProposalImprovementAllowed,
                SupportUsedJson = supportUsedJson,
                ProjectionVersion = timeline.LongLength,
                LastEventId = currentEvent.EventId,
                UpdatedAt = processedAt,
            };
            dbContext.S083Progress.Add(progress);
        }
        else
        {
            progress.ContentRevision = currentEvent.ContentRevision;
            progress.State = decision.State.ToString();
            progress.CriterionRevealAllowed = decision.CriterionRevealAllowed;
            progress.PaidProposalImprovementAllowed = decision.PaidProposalImprovementAllowed;
            progress.SupportUsedJson = supportUsedJson;
            progress.ProjectionVersion = timeline.LongLength;
            progress.LastEventId = currentEvent.EventId;
            progress.UpdatedAt = processedAt;
        }

        if (scheduledDueAt is not null)
        {
            var existing = await dbContext.DueRetrievals.FindAsync(
                [
                    currentEvent.TenantId,
                    currentEvent.LearnerId,
                    currentEvent.SessionId,
                    currentEvent.EventId,
                ],
                cancellationToken);
            if (existing is null)
            {
                dbContext.DueRetrievals.Add(new DueRetrievalEntity
                {
                    TenantId = currentEvent.TenantId,
                    LearnerId = currentEvent.LearnerId,
                    SessionId = currentEvent.SessionId,
                    ContentNodeId = currentEvent.ContentNodeId,
                    SourceEventId = currentEvent.EventId,
                    DueAt = scheduledDueAt.Value,
                });
            }
        }

        if (currentEvent.EventType == LearnerEventTypes.RetrievalCompleted)
        {
            var pending = dbContext.DueRetrievals.Local
                .Where(item =>
                    item.TenantId == currentEvent.TenantId &&
                    item.LearnerId == currentEvent.LearnerId &&
                    item.SessionId == currentEvent.SessionId &&
                    item.CompletedEventId == null)
                .OrderByDescending(item => item.DueAt)
                .FirstOrDefault();
            pending ??= await dbContext.DueRetrievals
                .Where(item =>
                    item.TenantId == currentEvent.TenantId &&
                    item.LearnerId == currentEvent.LearnerId &&
                    item.SessionId == currentEvent.SessionId &&
                    item.CompletedEventId == null)
                .OrderByDescending(item => item.DueAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (pending is not null)
            {
                pending.CompletedEventId = currentEvent.EventId;
            }
        }
    }

    private static string Limit(string value) =>
        value.Length <= 2000 ? value : value[..2000];

    private async Task RefreshEffectiveRightsAsync(
        DateTimeOffset now,
        IReadOnlySet<(Guid TenantId, Guid LearnerId)> affectedLearners,
        CancellationToken cancellationToken)
    {
        var dueRows = await dbContext.DueRetrievals
            .AsNoTracking()
            .Where(item => item.CompletedEventId == null && item.DueAt <= now)
            .ToListAsync(cancellationToken);
        var dueLearners = dueRows
            .Select(item => (item.TenantId, item.LearnerId))
            .ToHashSet();
        var dueSessions = dueRows
            .Select(item => (item.TenantId, item.LearnerId, item.SessionId))
            .ToHashSet();
        var candidateLearners = affectedLearners
            .Concat(dueLearners)
            .ToHashSet();
        if (candidateLearners.Count == 0)
        {
            return;
        }

        var candidateTenantIds = candidateLearners
            .Select(item => item.TenantId)
            .Distinct()
            .ToArray();
        var candidateLearnerIds = candidateLearners
            .Select(item => item.LearnerId)
            .Distinct()
            .ToArray();
        var candidateRows = await dbContext.S083Progress
            .Where(item =>
                candidateTenantIds.Contains(item.TenantId) &&
                candidateLearnerIds.Contains(item.LearnerId))
            .ToListAsync(cancellationToken);
        candidateRows = candidateRows
            .Where(item => candidateLearners.Contains(
                (item.TenantId, item.LearnerId)))
            .ToList();

        var dueTransitions = dueLearners
            .Where(learner => candidateRows.Any(progress =>
                progress.TenantId == learner.TenantId &&
                progress.LearnerId == learner.LearnerId &&
                (progress.CriterionRevealAllowed ||
                 progress.PaidProposalImprovementAllowed ||
                 (dueSessions.Contains((
                     progress.TenantId,
                     progress.LearnerId,
                     progress.SessionId)) &&
                  progress.State != S083LearningState.RetrievalDue.ToString()))))
            .ToHashSet();
        var targetLearners = affectedLearners
            .Concat(dueTransitions)
            .ToHashSet();
        if (targetLearners.Count == 0)
        {
            return;
        }

        var progressRows = candidateRows
            .Where(item => targetLearners.Contains(
                (item.TenantId, item.LearnerId)))
            .ToArray();
        var targetSessionIds = progressRows
            .Select(item => item.SessionId)
            .Distinct()
            .ToArray();
        var timelineEntities = await dbContext.LearnerEvents
            .AsNoTracking()
            .Where(item =>
                candidateTenantIds.Contains(item.TenantId) &&
                candidateLearnerIds.Contains(item.LearnerId) &&
                targetSessionIds.Contains(item.SessionId) &&
                dbContext.ProcessedProjectionEvents.Any(processed =>
                    processed.ProjectionName == ProjectionName &&
                    processed.EventId == item.EventId))
            .OrderBy(item => item.RecordedSequence)
            .ToListAsync(cancellationToken);
        var timelines = timelineEntities
            .GroupBy(item => (item.TenantId, item.LearnerId, item.SessionId))
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.ToContract()).ToArray());

        foreach (var progress in progressRows)
        {
            if (!timelines.TryGetValue(
                    (progress.TenantId, progress.LearnerId, progress.SessionId),
                    out var timeline))
            {
                continue;
            }

            var decision = S083Policy.Evaluate(timeline, now);
            var learnerLocked = dueLearners.Contains(
                (progress.TenantId, progress.LearnerId));
            var sessionDue = dueSessions.Contains(
                (progress.TenantId, progress.LearnerId, progress.SessionId));

            var state = sessionDue
                ? S083LearningState.RetrievalDue.ToString()
                : decision.State.ToString();
            var criterionRevealAllowed =
                !learnerLocked && decision.CriterionRevealAllowed;
            var paidProposalImprovementAllowed =
                !learnerLocked && decision.PaidProposalImprovementAllowed;
            var supportUsedJson = ContractJson.Serialize(
                S083Policy.ProjectSupportUsed(timeline));
            if (progress.State != state ||
                progress.CriterionRevealAllowed != criterionRevealAllowed ||
                progress.PaidProposalImprovementAllowed !=
                    paidProposalImprovementAllowed ||
                progress.SupportUsedJson != supportUsedJson)
            {
                progress.State = state;
                progress.CriterionRevealAllowed = criterionRevealAllowed;
                progress.PaidProposalImprovementAllowed =
                    paidProposalImprovementAllowed;
                progress.SupportUsedJson = supportUsedJson;
                progress.UpdatedAt = now;
            }
        }
    }
}

public sealed class ProjectionPoisonException(
    Guid eventId,
    string reason)
    : Exception($"Projection '{S083ProjectionBatchProcessor.ProjectionName}' is blocked by event {eventId}: {reason}")
{
    public Guid EventId { get; } = eventId;
}
