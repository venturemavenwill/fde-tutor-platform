using System.Collections.Concurrent;
using System.Text.Json;
using FdeTutor.Contracts.Events;
using FdeTutor.Domain.Authorization;
using FdeTutor.Domain.Events;

namespace FdeTutor.Persistence;

public sealed class InMemoryLearnerEventStore : ILearnerEventStore
{
    private readonly object sync = new();
    private readonly List<LearnerEventEnvelope> events = [];
    private readonly ConcurrentDictionary<(Guid TenantId, string Key), Guid> idempotency = new();

    public Task<IdempotencyLookupResult> FindIdempotentAsync(
        LearnerAuthorizationContext authorization,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            if (!idempotency.TryGetValue(
                    (authorization.TenantId, idempotencyKey),
                    out var eventId))
            {
                return Task.FromResult(
                    new IdempotencyLookupResult(IdempotencyLookupStatus.NotFound));
            }

            var existing = events.Single(item => item.EventId == eventId);
            return Task.FromResult(
                existing.LearnerId == authorization.LearnerId
                    ? new IdempotencyLookupResult(IdempotencyLookupStatus.Found, existing)
                    : new IdempotencyLookupResult(IdempotencyLookupStatus.Conflict));
        }
    }

    public Task<AppendEventResult> AppendAsync(
        AppendLearnerEventCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var idempotencyKey = (command.Authorization.TenantId, command.IdempotencyKey);

        lock (sync)
        {
            if (idempotency.TryGetValue(idempotencyKey, out var existingId))
            {
                var existing = events.Single(item => item.EventId == existingId);
                if (!Matches(command, existing))
                {
                    throw new IdempotencyConflictException();
                }

                return Task.FromResult(new AppendEventResult(existing, IsDuplicate: true));
            }

            var actualStreamVersion = events.LongCount(item =>
                item.TenantId == command.Authorization.TenantId &&
                item.LearnerId == command.Authorization.LearnerId &&
                item.SessionId == command.SessionId);
            if (actualStreamVersion != command.ExpectedStreamVersion)
            {
                throw new StreamConcurrencyException();
            }

            var recordedAt = DateTimeOffset.UtcNow;
            var envelope = new LearnerEventEnvelope(
                Guid.NewGuid(),
                command.EventType,
                EventVersion: 1,
                command.OccurredAt,
                recordedAt,
                command.Authorization.TenantId,
                command.Authorization.LearnerId,
                command.SessionId,
                command.ContentNodeId,
                command.ContentRevision,
                command.CorrelationId,
                command.CausationId,
                command.IdempotencyKey,
                new EventActor("learner", command.Authorization.ExternalSubject),
                command.Payload.Clone());

            events.Add(envelope);
            if (!idempotency.TryAdd(idempotencyKey, envelope.EventId))
            {
                throw new InvalidOperationException("The in-memory idempotency index changed unexpectedly.");
            }

            return Task.FromResult(new AppendEventResult(envelope, IsDuplicate: false));
        }
    }

    public Task<IReadOnlyList<LearnerEventEnvelope>> ReadSessionAsync(
        LearnerAuthorizationContext authorization,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            return Task.FromResult<IReadOnlyList<LearnerEventEnvelope>>(
                events
                    .Where(item =>
                        item.TenantId == authorization.TenantId &&
                        item.LearnerId == authorization.LearnerId &&
                        item.SessionId == sessionId)
                    .ToArray());
        }
    }

    public Task<IReadOnlyList<LearnerEventEnvelope>> ReadLearnerNodeAsync(
        LearnerAuthorizationContext authorization,
        string contentNodeId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            return Task.FromResult<IReadOnlyList<LearnerEventEnvelope>>(
                events
                    .Where(item =>
                        item.TenantId == authorization.TenantId &&
                        item.LearnerId == authorization.LearnerId &&
                        item.ContentNodeId == contentNodeId)
                    .ToArray());
        }
    }

    private static bool Matches(
        AppendLearnerEventCommand command,
        LearnerEventEnvelope existing) =>
        existing.TenantId == command.Authorization.TenantId &&
        existing.LearnerId == command.Authorization.LearnerId &&
        existing.EventType == command.EventType &&
        existing.ContentNodeId == command.ContentNodeId &&
        existing.ContentRevision == command.ContentRevision &&
        existing.EventVersion > 0 &&
        (existing.EventType == LearnerEventTypes.LearningSessionStarted ||
         existing.SessionId == command.SessionId) &&
        JsonElement.DeepEquals(existing.Payload, command.Payload);
}
