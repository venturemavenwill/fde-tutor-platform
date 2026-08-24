using FdeTutor.Api.Authentication;
using FdeTutor.Api.Content;
using FdeTutor.Contracts.Api;
using FdeTutor.Contracts.Events;

namespace FdeTutor.Api.Learning;

public static class S083Endpoints
{
    public static IEndpointRouteBuilder MapS083Endpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/v1/s083")
            .RequireAuthorization("LearnerAccess")
            .WithTags("S083");

        group.MapGet("/content", async (
            HttpContext context,
            LearnerAuthorizationContextFactory authorizationFactory,
            S083LearningService service,
            S083ContentProvider contentProvider,
            CancellationToken cancellationToken) =>
        {
            var authorization = authorizationFactory.Create(context.User);
            var sourceAbsentRecall = await service.HasDueRetrievalAsync(
                authorization,
                cancellationToken);
            return Results.Ok(contentProvider.GetPublicContent(sourceAbsentRecall));
        });

        group.MapGet("/learning-home", async (
            HttpContext context,
            LearnerAuthorizationContextFactory authorizationFactory,
            S083LearningService service,
            CancellationToken cancellationToken) =>
        {
            var authorization = authorizationFactory.Create(context.User);
            return Results.Ok(await service.GetLearningHomeAsync(
                authorization,
                cancellationToken));
        });

        group.MapPost("/sessions", async (
            HttpContext context,
            StartSessionRequest request,
            LearnerAuthorizationContextFactory authorizationFactory,
            S083LearningService service,
            CancellationToken cancellationToken) =>
        {
            if (!TryHeaders(context, out var idempotencyKey, out var correlationId, out var error))
            {
                return error;
            }

            var authorization = authorizationFactory.Create(context.User);
            var result = await service.StartSessionAsync(
                authorization,
                request,
                idempotencyKey,
                correlationId,
                cancellationToken);
            return ToHttpResult(result);
        });

        group.MapGet("/sessions/{sessionId:guid}", async (
            HttpContext context,
            Guid sessionId,
            LearnerAuthorizationContextFactory authorizationFactory,
            S083LearningService service,
            CancellationToken cancellationToken) =>
        {
            var authorization = authorizationFactory.Create(context.User);
            var state = await service.GetStateAsync(
                authorization,
                sessionId,
                cancellationToken);
            return state is null ? Results.NotFound() : Results.Ok(state);
        });

        MapTextCommand(group, "/sessions/{sessionId:guid}/expectation", LearnerEventTypes.ExpectationRecorded);
        MapNamedCommand(group, "/sessions/{sessionId:guid}/cold-start", LearnerEventTypes.PrerequisiteRecallAttempted);
        MapNamedCommand(group, "/sessions/{sessionId:guid}/priming", LearnerEventTypes.PrimingResponseSubmitted);
        MapTextCommand(group, "/sessions/{sessionId:guid}/unpaid-remedy", LearnerEventTypes.UnpaidRemedyRecorded);
        MapTextCommand(group, "/sessions/{sessionId:guid}/comparison", LearnerEventTypes.ComparisonRecorded);
        MapTextCommand(group, "/sessions/{sessionId:guid}/revision", LearnerEventTypes.ProposalRevisionRecorded);
        MapTextCommand(group, "/sessions/{sessionId:guid}/authentic-transfer", LearnerEventTypes.ArtifactSubmitted);
        MapTextCommand(group, "/sessions/{sessionId:guid}/retrieval", LearnerEventTypes.RetrievalCompleted);

        group.MapPost("/sessions/{sessionId:guid}/source/open", async (
            HttpContext context,
            Guid sessionId,
            RevisionRequest request,
            LearnerAuthorizationContextFactory authorizationFactory,
            S083LearningService service,
            S083ContentProvider contentProvider,
            CancellationToken cancellationToken) =>
        {
            if (!TryHeaders(context, out var idempotencyKey, out var correlationId, out var error))
            {
                return error;
            }

            var authorization = authorizationFactory.Create(context.User);
            if (await service.HasDueRetrievalAsync(
                    authorization,
                    cancellationToken))
            {
                return SourceAbsentRecallRequired();
            }

            var result = await service.RecordRevisionOnlyAsync(
                authorization,
                sessionId,
                LearnerEventTypes.SourceViewed,
                request,
                idempotencyKey,
                correlationId,
                cancellationToken);
            if (!result.IsAccepted)
            {
                return ToHttpResult(result);
            }

            if (!contentProvider.MatchesRevision(
                    result.AppendResult!.Event.ContentRevision))
            {
                return RevisionUnavailable();
            }

            var currentState = await service.GetStateAsync(
                authorization,
                result.AppendResult.Event.SessionId,
                cancellationToken);
            if (currentState?.Policy.State ==
                FdeTutor.Contracts.Policy.S083LearningState.RetrievalDue)
            {
                return SourceAbsentRecallRequired();
            }

            return Results.Ok(new SourceOpenResponse(
                new CommandAcceptedResponse(
                    result.AppendResult.Event,
                    result.AppendResult.IsDuplicate,
                    result.Policy),
                contentProvider.GetSourceHtml()));
        });

        group.MapGet("/sessions/{sessionId:guid}/source", async (
            HttpContext context,
            Guid sessionId,
            LearnerAuthorizationContextFactory authorizationFactory,
            S083LearningService service,
            S083ContentProvider contentProvider,
            CancellationToken cancellationToken) =>
        {
            var authorization = authorizationFactory.Create(context.User);
            if (await service.HasDueRetrievalAsync(
                    authorization,
                    cancellationToken))
            {
                return SourceAbsentRecallRequired();
            }

            var state = await service.GetStateAsync(
                authorization,
                sessionId,
                cancellationToken);
            if (state is null)
            {
                return Results.NotFound();
            }

            if (!contentProvider.MatchesRevision(state.ContentRevision))
            {
                return Results.Conflict(new ApiError(
                    "SESSION_REVISION_UNAVAILABLE",
                    "The session's pinned content revision is not loaded.",
                    "RESTORE_CONTENT_REVISION"));
            }

            var sourceViewed = state.Timeline.Any(
                item => item.EventType == LearnerEventTypes.SourceViewed);
            if (sourceViewed &&
                state.Policy.State !=
                    FdeTutor.Contracts.Policy.S083LearningState.RetrievalDue)
            {
                return Results.Ok(new { sourceHtml = contentProvider.GetSourceHtml() });
            }

            var dueRecall = state.Policy.State ==
                FdeTutor.Contracts.Policy.S083LearningState.RetrievalDue;
            var code = dueRecall
                ? "SOURCE_ABSENT_RECALL_REQUIRED"
                : PolicyErrorCodes.From(state.Policy.DenialReason);
            return Results.Conflict(new ApiError(
                code,
                "Complete the required learning act before opening the source.",
                dueRecall ? "COMPLETE_RETRIEVAL" : code));
        });

        group.MapPost("/sessions/{sessionId:guid}/criterion/reveal", async (
            HttpContext context,
            Guid sessionId,
            RevisionRequest request,
            LearnerAuthorizationContextFactory authorizationFactory,
            S083LearningService service,
            S083ContentProvider contentProvider,
            CancellationToken cancellationToken) =>
        {
            if (!TryHeaders(context, out var idempotencyKey, out var correlationId, out var error))
            {
                return error;
            }

            var authorization = authorizationFactory.Create(context.User);
            if (await service.HasDueRetrievalAsync(
                    authorization,
                    cancellationToken))
            {
                return SourceAbsentRecallRequired();
            }

            var result = await service.RecordRevisionOnlyAsync(
                authorization,
                sessionId,
                LearnerEventTypes.ModelAnswerRevealed,
                request,
                idempotencyKey,
                correlationId,
                cancellationToken);
            if (!result.IsAccepted)
            {
                return ToHttpResult(result);
            }

            if (!contentProvider.MatchesRevision(
                    result.AppendResult!.Event.ContentRevision))
            {
                return RevisionUnavailable();
            }

            var currentState = await service.GetStateAsync(
                authorization,
                result.AppendResult.Event.SessionId,
                cancellationToken);
            if (currentState?.Policy.State ==
                FdeTutor.Contracts.Policy.S083LearningState.RetrievalDue)
            {
                return SourceAbsentRecallRequired();
            }

            return Results.Ok(new CriterionRevealResponse(
                new CommandAcceptedResponse(
                    result.AppendResult.Event,
                    result.AppendResult.IsDuplicate,
                    result.Policy),
                contentProvider.GetCriterion()));
        });

        group.MapGet("/sessions/{sessionId:guid}/criterion", async (
            HttpContext context,
            Guid sessionId,
            LearnerAuthorizationContextFactory authorizationFactory,
            S083LearningService service,
            S083ContentProvider contentProvider,
            CancellationToken cancellationToken) =>
        {
            var authorization = authorizationFactory.Create(context.User);
            if (await service.HasDueRetrievalAsync(
                    authorization,
                    cancellationToken))
            {
                return SourceAbsentRecallRequired();
            }

            var state = await service.GetStateAsync(
                authorization,
                sessionId,
                cancellationToken);
            if (state is null)
            {
                return Results.NotFound();
            }

            if (!contentProvider.MatchesRevision(state.ContentRevision))
            {
                return Results.Conflict(new ApiError(
                    "SESSION_REVISION_UNAVAILABLE",
                    "The session's pinned content revision is not loaded.",
                    "RESTORE_CONTENT_REVISION"));
            }

            var criterionRevealed = state.Timeline.Any(
                item => item.EventType == LearnerEventTypes.ModelAnswerRevealed);
            if (criterionRevealed &&
                state.Policy.State !=
                    FdeTutor.Contracts.Policy.S083LearningState.RetrievalDue)
            {
                return Results.Ok(contentProvider.GetCriterion());
            }

            var dueRecall = state.Policy.State ==
                FdeTutor.Contracts.Policy.S083LearningState.RetrievalDue;
            var revealAvailable = state.Policy.State ==
                FdeTutor.Contracts.Policy.S083LearningState.CriterionAvailable;
            var code = dueRecall
                ? "SOURCE_ABSENT_RECALL_REQUIRED"
                : revealAvailable
                    ? "CRITERION_REVEAL_REQUIRED"
                    : PolicyErrorCodes.From(state.Policy.DenialReason);
            return Results.Conflict(new ApiError(
                code,
                "Complete the required learning act before viewing the criterion.",
                dueRecall ? "COMPLETE_RETRIEVAL" : code));
        });

        group.MapPost("/sessions/{sessionId:guid}/retrieval-schedule", async (
            HttpContext context,
            Guid sessionId,
            RetrievalScheduleRequest request,
            LearnerAuthorizationContextFactory authorizationFactory,
            S083LearningService service,
            CancellationToken cancellationToken) =>
        {
            if (!TryHeaders(context, out var idempotencyKey, out var correlationId, out var error))
            {
                return error;
            }

            var authorization = authorizationFactory.Create(context.User);
            var result = await service.ScheduleRetrievalAsync(
                authorization,
                sessionId,
                request,
                idempotencyKey,
                correlationId,
                cancellationToken);
            return ToHttpResult(result);
        });

        return endpoints;
    }

    private static void MapTextCommand(
        RouteGroupBuilder group,
        string pattern,
        string eventType) =>
        group.MapPost(pattern, async (
            HttpContext context,
            Guid sessionId,
            TextSubmissionRequest request,
            LearnerAuthorizationContextFactory authorizationFactory,
            S083LearningService service,
            CancellationToken cancellationToken) =>
        {
            if (!TryHeaders(context, out var idempotencyKey, out var correlationId, out var error))
            {
                return error;
            }

            var authorization = authorizationFactory.Create(context.User);
            var result = await service.RecordTextAsync(
                authorization,
                sessionId,
                eventType,
                request,
                idempotencyKey,
                correlationId,
                cancellationToken);
            return ToHttpResult(result);
        });

    private static void MapNamedCommand(
        RouteGroupBuilder group,
        string pattern,
        string eventType) =>
        group.MapPost(pattern, async (
            HttpContext context,
            Guid sessionId,
            NamedResponsesRequest request,
            LearnerAuthorizationContextFactory authorizationFactory,
            S083LearningService service,
            CancellationToken cancellationToken) =>
        {
            if (!TryHeaders(context, out var idempotencyKey, out var correlationId, out var error))
            {
                return error;
            }

            var authorization = authorizationFactory.Create(context.User);
            var result = await service.RecordNamedResponsesAsync(
                authorization,
                sessionId,
                eventType,
                request,
                idempotencyKey,
                correlationId,
                cancellationToken);
            return ToHttpResult(result);
        });

    private static IResult ToHttpResult(S083CommandResult result) =>
        result.IsAccepted
            ? Results.Ok(new CommandAcceptedResponse(
                result.AppendResult!.Event,
                result.AppendResult.IsDuplicate,
                result.Policy))
            : result.Error?.Code.StartsWith("INVALID_", StringComparison.Ordinal) == true
                ? Results.BadRequest(result.Error)
                : Results.Conflict(result.Error);

    private static IResult RevisionUnavailable() =>
        Results.Conflict(new ApiError(
            "SESSION_REVISION_UNAVAILABLE",
            "The command was accepted under a content revision that is not loaded.",
            "RESTORE_CONTENT_REVISION"));

    private static IResult SourceAbsentRecallRequired() =>
        Results.Conflict(new ApiError(
            "SOURCE_ABSENT_RECALL_REQUIRED",
            "Complete the source-absent retrieval before reopening study material.",
            "COMPLETE_RETRIEVAL"));

    private static bool TryHeaders(
        HttpContext context,
        out string idempotencyKey,
        out Guid correlationId,
        out IResult error)
    {
        idempotencyKey = context.Request.Headers["Idempotency-Key"].ToString().Trim();
        if (idempotencyKey.Length is < 8 or > 128)
        {
            correlationId = Guid.Empty;
            error = Results.BadRequest(new ApiError(
                "IDEMPOTENCY_KEY_REQUIRED",
                "Idempotency-Key must contain 8 to 128 characters."));
            return false;
        }

        var correlationValue = context.Request.Headers["X-Correlation-Id"].ToString();
        correlationId = Guid.TryParse(correlationValue, out var parsed)
            ? parsed
            : Guid.NewGuid();
        error = Results.Empty;
        return true;
    }
}
