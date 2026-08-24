using System.Text.Json;
using FdeTutor.Contracts.Events;
using FdeTutor.Contracts.Serialization;
using FdeTutor.Domain.Authorization;
using FdeTutor.Domain.Events;
using FdeTutor.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FdeTutor.Persistence;

public sealed class PostgresLearnerEventStore(FdeTutorDbContext dbContext)
    : ILearnerEventStore
{
    public async Task<IdempotencyLookupResult> FindIdempotentAsync(
        LearnerAuthorizationContext authorization,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var existing = await FindByIdempotencyKeyAsync(
            authorization.TenantId,
            idempotencyKey,
            cancellationToken);
        if (existing is null)
        {
            return new IdempotencyLookupResult(IdempotencyLookupStatus.NotFound);
        }

        return existing.LearnerId == authorization.LearnerId
            ? new IdempotencyLookupResult(
                IdempotencyLookupStatus.Found,
                existing.ToContract())
            : new IdempotencyLookupResult(IdempotencyLookupStatus.Conflict);
    }

    public async Task<AppendEventResult> AppendAsync(
        AppendLearnerEventCommand command,
        CancellationToken cancellationToken)
    {
        var existing = await FindByIdempotencyKeyAsync(
            command.Authorization.TenantId,
            command.IdempotencyKey,
            cancellationToken);
        if (existing is not null)
        {
            if (!Matches(command, existing.ToContract()))
            {
                throw new IdempotencyConflictException();
            }

            return new AppendEventResult(existing.ToContract(), IsDuplicate: true);
        }

        var recordedAt = DateTimeOffset.UtcNow;
        var entity = new LearnerEventEntity
        {
            EventId = Guid.NewGuid(),
            EventType = command.EventType,
            EventVersion = 1,
            OccurredAt = command.OccurredAt,
            RecordedAt = recordedAt,
            TenantId = command.Authorization.TenantId,
            LearnerId = command.Authorization.LearnerId,
            SessionId = command.SessionId,
            StreamVersion = command.ExpectedStreamVersion + 1,
            ContentNodeId = command.ContentNodeId,
            ContentRevision = command.ContentRevision,
            CorrelationId = command.CorrelationId,
            CausationId = command.CausationId,
            IdempotencyKey = command.IdempotencyKey,
            ActorType = "learner",
            ActorId = command.Authorization.ExternalSubject,
            PayloadJson = command.Payload.GetRawText(),
        };

        var envelope = entity.ToContract();
        var outbox = new OutboxMessageEntity
        {
            MessageId = Guid.NewGuid(),
            TenantId = command.Authorization.TenantId,
            EventId = entity.EventId,
            Topic = $"learner-events.{command.EventType}",
            PayloadJson = ContractJson.Serialize(envelope),
            CreatedAt = recordedAt,
            AvailableAt = recordedAt,
            AttemptCount = 0,
        };

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var streamLockKey =
            $"{command.Authorization.TenantId:N}:{command.Authorization.LearnerId:N}:{command.SessionId:N}";
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({streamLockKey}, 0));",
            cancellationToken);

        var duplicateAfterLock = await FindByIdempotencyKeyAsync(
            command.Authorization.TenantId,
            command.IdempotencyKey,
            cancellationToken);
        if (duplicateAfterLock is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            if (!Matches(command, duplicateAfterLock.ToContract()))
            {
                throw new IdempotencyConflictException();
            }
            return new AppendEventResult(
                duplicateAfterLock.ToContract(),
                IsDuplicate: true);
        }

        var currentStreamVersion = await dbContext.LearnerEvents
            .Where(item =>
                item.TenantId == command.Authorization.TenantId &&
                item.LearnerId == command.Authorization.LearnerId &&
                item.SessionId == command.SessionId)
            .Select(item => (long?)item.StreamVersion)
            .MaxAsync(cancellationToken) ?? 0;
        if (currentStreamVersion != command.ExpectedStreamVersion)
        {
            throw new StreamConcurrencyException();
        }

        dbContext.LearnerEvents.Add(entity);
        dbContext.OutboxMessages.Add(outbox);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new AppendEventResult(envelope, IsDuplicate: false);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
            })
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            var duplicate = await FindByIdempotencyKeyAsync(
                command.Authorization.TenantId,
                command.IdempotencyKey,
                cancellationToken);
            if (duplicate is null)
            {
                throw new StreamConcurrencyException();
            }

            if (!Matches(command, duplicate.ToContract()))
            {
                throw new IdempotencyConflictException();
            }

            return new AppendEventResult(duplicate.ToContract(), IsDuplicate: true);
        }
    }

    public async Task<IReadOnlyList<LearnerEventEnvelope>> ReadSessionAsync(
        LearnerAuthorizationContext authorization,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var entities = await dbContext.LearnerEvents
            .AsNoTracking()
            .Where(item =>
                item.TenantId == authorization.TenantId &&
                item.LearnerId == authorization.LearnerId &&
                item.SessionId == sessionId)
            .OrderBy(item => item.RecordedSequence)
            .ToListAsync(cancellationToken);
        return entities.Select(item => item.ToContract()).ToArray();
    }

    public async Task<IReadOnlyList<LearnerEventEnvelope>> ReadLearnerNodeAsync(
        LearnerAuthorizationContext authorization,
        string contentNodeId,
        CancellationToken cancellationToken)
    {
        var entities = await dbContext.LearnerEvents
            .AsNoTracking()
            .Where(item =>
                item.TenantId == authorization.TenantId &&
                item.LearnerId == authorization.LearnerId &&
                item.ContentNodeId == contentNodeId)
            .OrderBy(item => item.RecordedSequence)
            .ToListAsync(cancellationToken);
        return entities.Select(item => item.ToContract()).ToArray();
    }

    private Task<LearnerEventEntity?> FindByIdempotencyKeyAsync(
        Guid tenantId,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        dbContext.LearnerEvents
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.TenantId == tenantId && item.IdempotencyKey == idempotencyKey,
                cancellationToken);

    private static bool Matches(
        AppendLearnerEventCommand command,
        LearnerEventEnvelope existing) =>
        existing.TenantId == command.Authorization.TenantId &&
        existing.LearnerId == command.Authorization.LearnerId &&
        existing.EventType == command.EventType &&
        existing.ContentNodeId == command.ContentNodeId &&
        existing.ContentRevision == command.ContentRevision &&
        (existing.EventType == LearnerEventTypes.LearningSessionStarted ||
         existing.SessionId == command.SessionId) &&
        JsonElement.DeepEquals(existing.Payload, command.Payload);
}
