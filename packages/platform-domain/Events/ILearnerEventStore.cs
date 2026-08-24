using FdeTutor.Contracts.Events;
using FdeTutor.Domain.Authorization;

namespace FdeTutor.Domain.Events;

public interface ILearnerEventStore
{
    Task<IdempotencyLookupResult> FindIdempotentAsync(
        LearnerAuthorizationContext authorization,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<AppendEventResult> AppendAsync(
        AppendLearnerEventCommand command,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<LearnerEventEnvelope>> ReadSessionAsync(
        LearnerAuthorizationContext authorization,
        Guid sessionId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<LearnerEventEnvelope>> ReadLearnerNodeAsync(
        LearnerAuthorizationContext authorization,
        string contentNodeId,
        CancellationToken cancellationToken);
}

public enum IdempotencyLookupStatus
{
    NotFound,
    Found,
    Conflict,
}

public sealed record IdempotencyLookupResult(
    IdempotencyLookupStatus Status,
    LearnerEventEnvelope? Event = null);

public sealed class IdempotencyConflictException()
    : Exception("The idempotency key was already used by a different command.");

public sealed class StreamConcurrencyException()
    : Exception("The learner event stream changed before the command was appended.");
