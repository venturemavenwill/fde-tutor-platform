using System.Text.Json;
using FdeTutor.Api.Content;
using FdeTutor.Contracts.Api;
using FdeTutor.Contracts.Events;
using FdeTutor.Contracts.Policy;
using FdeTutor.Domain.Authorization;
using FdeTutor.Domain.Events;
using FdeTutor.Domain.Policy;

namespace FdeTutor.Api.Learning;

public sealed class S083LearningService(
    ILearnerEventStore eventStore,
    S083ContentProvider contentProvider)
{
    private const int MaximumResponseLength = 12_000;

    public async Task<S083CommandResult> StartSessionAsync(
        LearnerAuthorizationContext authorization,
        StartSessionRequest request,
        string idempotencyKey,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToElement(new
        {
            route = "S083",
            assessmentPosture = "NON_ASSESSMENT",
        });
        var replay = await ResolveIdempotencyAsync(
            authorization,
            idempotencyKey,
            LearnerEventTypes.LearningSessionStarted,
            expectedSessionId: null,
            request.ContentRevision,
            payload,
            cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        if (!contentProvider.MatchesRevision(request.ContentRevision))
        {
            return RevisionRejected([]);
        }

        var learnerEvents = await eventStore.ReadLearnerNodeAsync(
            authorization,
            S083Policy.ContentNodeId,
            cancellationToken);
        if (GetDueSessionIds(learnerEvents, DateTimeOffset.UtcNow).Count > 0)
        {
            return DueRetrievalRejected([]);
        }

        var sessionId = Guid.NewGuid();
        var command = CreateCommand(
            authorization,
            sessionId,
            LearnerEventTypes.LearningSessionStarted,
            request.ContentRevision,
            0,
            correlationId,
            causationId: null,
            idempotencyKey,
            payload);
        AppendEventResult appended;
        try
        {
            appended = await eventStore.AppendAsync(command, cancellationToken);
        }
        catch (IdempotencyConflictException)
        {
            return IdempotencyRejected([]);
        }
        catch (StreamConcurrencyException)
        {
            return StreamConcurrencyRejected([]);
        }

        var timeline = await eventStore.ReadSessionAsync(
            authorization,
            appended.Event.SessionId,
            cancellationToken);
        return new S083CommandResult(
            appended,
            EvaluateThroughEvent(timeline, appended.Event.EventId),
            Error: null);
    }

    public async Task<S083CommandResult> RecordTextAsync(
        LearnerAuthorizationContext authorization,
        Guid sessionId,
        string eventType,
        TextSubmissionRequest request,
        string idempotencyKey,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        if (!IsValidText(request.Response))
        {
            return Rejected(
                [],
                "INVALID_RESPONSE",
                $"A response must contain 1 to {MaximumResponseLength} characters.");
        }

        var payload = eventType == LearnerEventTypes.ArtifactSubmitted
            ? JsonSerializer.SerializeToElement(new
            {
                response = request.Response.Trim(),
                classification = "AUTHENTIC_WORK",
                pilotRestriction = "SYNTHETIC_REDACTED_OR_EXPLICITLY_APPROVED",
            })
            : JsonSerializer.SerializeToElement(new { response = request.Response.Trim() });
        return await AppendAuthorizedAsync(
            authorization,
            sessionId,
            eventType,
            request.ContentRevision,
            idempotencyKey,
            correlationId,
            payload,
            cancellationToken);
    }

    public async Task<S083CommandResult> RecordRevisionOnlyAsync(
        LearnerAuthorizationContext authorization,
        Guid sessionId,
        string eventType,
        RevisionRequest request,
        string idempotencyKey,
        Guid correlationId,
        CancellationToken cancellationToken) =>
        await AppendAuthorizedAsync(
            authorization,
            sessionId,
            eventType,
            request.ContentRevision,
            idempotencyKey,
            correlationId,
            JsonSerializer.SerializeToElement(new { acknowledged = true }),
            cancellationToken);

    public async Task<S083CommandResult> RecordNamedResponsesAsync(
        LearnerAuthorizationContext authorization,
        Guid sessionId,
        string eventType,
        NamedResponsesRequest request,
        string idempotencyKey,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        if (request.Responses is null ||
            request.Responses.Count == 0 ||
            request.Responses.Any(item =>
                string.IsNullOrWhiteSpace(item.Key) ||
                string.IsNullOrWhiteSpace(item.Value) ||
                item.Value.Length > MaximumResponseLength))
        {
            return Rejected(
                [],
                "INVALID_RESPONSE",
                "At least one non-empty bounded response is required.");
        }

        var responses = new SortedDictionary<string, string>(
            request.Responses.ToDictionary(
                item => item.Key,
                item => item.Value.Trim(),
                StringComparer.Ordinal),
            StringComparer.Ordinal);
        var payload = JsonSerializer.SerializeToElement(new { responses });
        var replay = await ResolveIdempotencyAsync(
            authorization,
            idempotencyKey,
            eventType,
            sessionId,
            request.ContentRevision,
            payload,
            cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        var expectedResponseIds = contentProvider.GetExpectedNamedResponseIds(eventType);
        if (!expectedResponseIds.SetEquals(request.Responses.Keys))
        {
            return Rejected(
                [],
                "INVALID_RESPONSE_SET",
                "The response set must contain every expected prompt ID and no unknown IDs.");
        }

        return await AppendAuthorizedAsync(
            authorization,
            sessionId,
            eventType,
            request.ContentRevision,
            idempotencyKey,
            correlationId,
            payload,
            cancellationToken);
    }

    public async Task<S083CommandResult> ScheduleRetrievalAsync(
        LearnerAuthorizationContext authorization,
        Guid sessionId,
        RetrievalScheduleRequest request,
        string idempotencyKey,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        var dueAt = request.DueAt.ToUniversalTime();
        var payload = JsonSerializer.SerializeToElement(new
        {
            dueAt,
            mode = "CHANGED_CONTEXT_SAME_NODE",
        });
        var replay = await ResolveIdempotencyAsync(
            authorization,
            idempotencyKey,
            LearnerEventTypes.RetrievalScheduled,
            sessionId,
            request.ContentRevision,
            payload,
            cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        if (dueAt <= DateTimeOffset.UtcNow)
        {
            return Rejected(
                [],
                "INVALID_DUE_AT",
                "The retrieval due date must be in the future.");
        }

        return await AppendAuthorizedAsync(
            authorization,
            sessionId,
            LearnerEventTypes.RetrievalScheduled,
            request.ContentRevision,
            idempotencyKey,
            correlationId,
            payload,
            cancellationToken);
    }

    public async Task<S083StateResponse?> GetStateAsync(
        LearnerAuthorizationContext authorization,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var timeline = await eventStore.ReadSessionAsync(
            authorization,
            sessionId,
            cancellationToken);
        var learnerEvents = await eventStore.ReadLearnerNodeAsync(
            authorization,
            S083Policy.ContentNodeId,
            cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var learnerHasDueRetrieval = HasDueRetrieval(learnerEvents, now);
        return timeline.Count == 0
            ? null
            : ToState(timeline, learnerHasDueRetrieval, now);
    }

    public async Task<LearningHomeResponse> GetLearningHomeAsync(
        LearnerAuthorizationContext authorization,
        CancellationToken cancellationToken)
    {
        var events = await eventStore.ReadLearnerNodeAsync(
            authorization,
            S083Policy.ContentNodeId,
            cancellationToken);
        if (events.Count == 0)
        {
            return new LearningHomeResponse(Current: null, DueRetrievals: []);
        }

        var sessions = events
            .GroupBy(item => item.SessionId)
            .Select(group => group.ToArray())
            .OrderByDescending(group => group[^1].RecordedAt)
            .ToArray();
        var now = DateTimeOffset.UtcNow;
        var learnerHasDueRetrieval = HasDueRetrieval(events, now);
        var sessionStates = sessions
            .Select(session => ToState(session, learnerHasDueRetrieval, now))
            .ToArray();
        var current = sessionStates.FirstOrDefault(
            state => state.Policy.State != S083LearningState.Complete)
            ?? sessionStates[0];
        var dueRetrievals = sessions
            .Select(group => ToDueRetrieval(group, now))
            .Where(item => item is not null)
            .Cast<DueRetrievalResponse>()
            .ToArray();
        return new LearningHomeResponse(current, dueRetrievals);
    }

    public async Task<bool> HasDueRetrievalAsync(
        LearnerAuthorizationContext authorization,
        CancellationToken cancellationToken)
    {
        var events = await eventStore.ReadLearnerNodeAsync(
            authorization,
            S083Policy.ContentNodeId,
            cancellationToken);
        return HasDueRetrieval(events, DateTimeOffset.UtcNow);
    }

    private async Task<S083CommandResult> AppendAuthorizedAsync(
        LearnerAuthorizationContext authorization,
        Guid sessionId,
        string eventType,
        string contentRevision,
        string idempotencyKey,
        Guid correlationId,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        var replay = await ResolveIdempotencyAsync(
            authorization,
            idempotencyKey,
            eventType,
            sessionId,
            contentRevision,
            payload,
            cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        var timeline = await eventStore.ReadSessionAsync(
            authorization,
            sessionId,
            cancellationToken);
        if (timeline.Count == 0)
        {
            return Rejected(
                timeline,
                "SESSION_NOT_FOUND",
                "The S083 session does not exist for this learner.");
        }

        if (!StringComparer.Ordinal.Equals(timeline[0].ContentRevision, contentRevision))
        {
            return SessionRevisionRejected(timeline);
        }

        if (!contentProvider.MatchesRevision(timeline[0].ContentRevision))
        {
            return SessionRevisionUnavailable(timeline);
        }

        var learnerEvents = await eventStore.ReadLearnerNodeAsync(
            authorization,
            S083Policy.ContentNodeId,
            cancellationToken);
        var dueSessions = GetDueSessionIds(learnerEvents, DateTimeOffset.UtcNow);
        if (dueSessions.Count > 0 &&
            (eventType != LearnerEventTypes.RetrievalCompleted ||
             !dueSessions.Contains(sessionId)))
        {
            return DueRetrievalRejected(timeline);
        }

        var denial = S083Policy.AuthorizeEvent(timeline, eventType);
        if (denial != PolicyDenialReason.None)
        {
            return Rejected(
                timeline,
                PolicyErrorCodes.From(denial),
                "The requested action is not permitted in the current learning state.");
        }

        var command = CreateCommand(
            authorization,
            sessionId,
            eventType,
            contentRevision,
            timeline.Count,
            correlationId,
            timeline[^1].EventId,
            idempotencyKey,
            payload);
        AppendEventResult appended;
        try
        {
            appended = await eventStore.AppendAsync(command, cancellationToken);
        }
        catch (IdempotencyConflictException)
        {
            return IdempotencyRejected(timeline);
        }
        catch (StreamConcurrencyException)
        {
            return StreamConcurrencyRejected(timeline);
        }

        var updated = await eventStore.ReadSessionAsync(
            authorization,
            sessionId,
            cancellationToken);
        return new S083CommandResult(
            appended,
            EvaluateThroughEvent(updated, appended.Event.EventId),
            Error: null);
    }

    private static AppendLearnerEventCommand CreateCommand(
        LearnerAuthorizationContext authorization,
        Guid sessionId,
        string eventType,
        string contentRevision,
        long expectedStreamVersion,
        Guid correlationId,
        Guid? causationId,
        string idempotencyKey,
        JsonElement payload) =>
        new(
            authorization,
            sessionId,
            eventType,
            S083Policy.ContentNodeId,
            contentRevision,
            expectedStreamVersion,
            correlationId,
            causationId,
            idempotencyKey,
            DateTimeOffset.UtcNow,
            payload);

    private static S083CommandResult Rejected(
        IReadOnlyCollection<LearnerEventEnvelope> timeline,
        string code,
        string message) =>
        new(
            AppendResult: null,
            S083Policy.Evaluate(timeline),
            new ApiError(code, message, RequiredAction: code));

    private S083CommandResult RevisionRejected(
        IReadOnlyCollection<LearnerEventEnvelope> timeline) =>
        new(
            AppendResult: null,
            S083Policy.Evaluate(timeline),
            new ApiError(
                "REVISION_MISMATCH",
                $"Use the active content revision '{contentProvider.ContentRevision}'.",
                "RELOAD_CONTENT"));

    private static S083CommandResult SessionRevisionRejected(
        IReadOnlyCollection<LearnerEventEnvelope> timeline) =>
        new(
            AppendResult: null,
            S083Policy.Evaluate(timeline),
            new ApiError(
                "SESSION_REVISION_MISMATCH",
                "The command revision does not match the session's pinned content revision.",
                "RELOAD_SESSION"));

    private static S083CommandResult SessionRevisionUnavailable(
        IReadOnlyCollection<LearnerEventEnvelope> timeline) =>
        new(
            AppendResult: null,
            S083Policy.Evaluate(timeline),
            new ApiError(
                "SESSION_REVISION_UNAVAILABLE",
                "The session's pinned content revision is not loaded. The session cannot continue.",
                "RESTORE_CONTENT_REVISION"));

    private static S083CommandResult IdempotencyRejected(
        IReadOnlyCollection<LearnerEventEnvelope> timeline) =>
        new(
            AppendResult: null,
            S083Policy.Evaluate(timeline),
            new ApiError(
                "IDEMPOTENCY_KEY_CONFLICT",
                "The idempotency key was already used by a different command.",
                "USE_NEW_IDEMPOTENCY_KEY"));

    private static S083CommandResult StreamConcurrencyRejected(
        IReadOnlyCollection<LearnerEventEnvelope> timeline) =>
        new(
            AppendResult: null,
            S083Policy.Evaluate(timeline),
            new ApiError(
                "STREAM_CONCURRENCY_CONFLICT",
                "The learner session changed before this command was saved.",
                "RELOAD_SESSION"));

    private static S083CommandResult DueRetrievalRejected(
        IReadOnlyCollection<LearnerEventEnvelope> timeline) =>
        new(
            AppendResult: null,
            ApplyLearnerDueLock(S083Policy.Evaluate(timeline)),
            new ApiError(
                "DUE_RETRIEVAL_REQUIRED",
                "Complete the due source-absent S083 retrieval before continuing other work.",
                "COMPLETE_RETRIEVAL"));

    private async Task<S083CommandResult?> ResolveIdempotencyAsync(
        LearnerAuthorizationContext authorization,
        string idempotencyKey,
        string eventType,
        Guid? expectedSessionId,
        string contentRevision,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        var lookup = await eventStore.FindIdempotentAsync(
            authorization,
            idempotencyKey,
            cancellationToken);
        if (lookup.Status == IdempotencyLookupStatus.NotFound)
        {
            return null;
        }

        if (lookup.Status == IdempotencyLookupStatus.Conflict || lookup.Event is null)
        {
            return IdempotencyRejected([]);
        }

        var existing = lookup.Event;
        if (existing.EventType != eventType ||
            existing.ContentNodeId != S083Policy.ContentNodeId ||
            existing.ContentRevision != contentRevision ||
            (expectedSessionId is not null && existing.SessionId != expectedSessionId) ||
            !JsonElement.DeepEquals(existing.Payload, payload))
        {
            var conflictingTimeline = await eventStore.ReadSessionAsync(
                authorization,
                existing.SessionId,
                cancellationToken);
            return IdempotencyRejected(conflictingTimeline);
        }

        var timeline = await eventStore.ReadSessionAsync(
            authorization,
            existing.SessionId,
            cancellationToken);
        return new S083CommandResult(
            new AppendEventResult(existing, IsDuplicate: true),
            EvaluateThroughEvent(timeline, existing.EventId),
            Error: null);
    }

    private static S083StateResponse ToState(
        IReadOnlyList<LearnerEventEnvelope> timeline,
        bool redactLearnerResponses,
        DateTimeOffset now)
    {
        var policy = S083Policy.Evaluate(timeline, now);
        if (redactLearnerResponses &&
            policy.State != S083LearningState.RetrievalDue)
        {
            policy = ApplyLearnerDueLock(policy);
        }
        return new S083StateResponse(
            timeline[0].SessionId,
            S083Policy.ContentNodeId,
            timeline[0].ContentRevision,
            policy,
            redactLearnerResponses
                ? timeline.Select(RedactLearnerResponse).ToArray()
                : timeline,
            ProjectionVersion: timeline.Count);
    }

    private static bool HasDueRetrieval(
        IEnumerable<LearnerEventEnvelope> events,
        DateTimeOffset now) =>
        GetDueSessionIds(events, now).Count > 0;

    private static HashSet<Guid> GetDueSessionIds(
        IEnumerable<LearnerEventEnvelope> events,
        DateTimeOffset now) =>
        events
            .GroupBy(item => item.SessionId)
            .Where(group => S083Policy.Evaluate(group, now).State ==
                S083LearningState.RetrievalDue)
            .Select(group => group.Key)
            .ToHashSet();

    private static S083PolicyDecision ApplyLearnerDueLock(
        S083PolicyDecision policy) =>
        policy with
        {
            PermittedActions = [],
            CriterionRevealAllowed = false,
            PaidProposalImprovementAllowed = false,
            DenialReason = PolicyDenialReason.DueRetrievalRequired,
        };

    private static LearnerEventEnvelope RedactLearnerResponse(
        LearnerEventEnvelope item)
    {
        if (item.Payload.TryGetProperty("response", out _))
        {
            var redacted = item.Payload
                .EnumerateObject()
                .ToDictionary(
                    property => property.Name,
                    property => property.Name == "response"
                        ? (object)"[REDACTED_DURING_SOURCE_ABSENT_RECALL]"
                        : property.Value.Clone(),
                    StringComparer.Ordinal);
            return item with
            {
                Payload = JsonSerializer.SerializeToElement(redacted),
            };
        }

        if (item.Payload.TryGetProperty("responses", out var responses) &&
            responses.ValueKind == JsonValueKind.Object)
        {
            var redacted = responses
                .EnumerateObject()
                .ToDictionary(
                    property => property.Name,
                    _ => "[REDACTED_DURING_SOURCE_ABSENT_RECALL]",
                    StringComparer.Ordinal);
            return item with
            {
                Payload = JsonSerializer.SerializeToElement(new { responses = redacted }),
            };
        }

        return item;
    }

    private static S083PolicyDecision EvaluateThroughEvent(
        IReadOnlyList<LearnerEventEnvelope> timeline,
        Guid eventId)
    {
        var eventIndex = timeline
            .Select((item, index) => (item, index))
            .Single(pair => pair.item.EventId == eventId)
            .index;
        return S083Policy.Evaluate(
            timeline.Take(eventIndex + 1),
            timeline[eventIndex].RecordedAt);
    }

    private static DueRetrievalResponse? ToDueRetrieval(
        IReadOnlyList<LearnerEventEnvelope> timeline,
        DateTimeOffset now)
    {
        if (timeline.Any(item => item.EventType == LearnerEventTypes.RetrievalCompleted))
        {
            return null;
        }

        var scheduled = timeline.LastOrDefault(
            item => item.EventType == LearnerEventTypes.RetrievalScheduled);
        if (scheduled is null ||
            !scheduled.Payload.TryGetProperty("dueAt", out var dueAtValue) ||
            dueAtValue.ValueKind != JsonValueKind.String ||
            !DateTimeOffset.TryParse(dueAtValue.GetString(), out var dueAt))
        {
            return null;
        }

        return new DueRetrievalResponse(
            scheduled.SessionId,
            scheduled.ContentNodeId,
            dueAt,
            dueAt <= now);
    }

    private static bool IsValidText(string response) =>
        !string.IsNullOrWhiteSpace(response) && response.Length <= MaximumResponseLength;
}
