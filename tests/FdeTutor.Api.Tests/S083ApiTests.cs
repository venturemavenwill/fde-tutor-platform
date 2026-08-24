using System.Net;
using System.Net.Http.Json;
using FdeTutor.Contracts.Api;
using FdeTutor.Contracts.Events;
using FdeTutor.Contracts.Policy;
using FdeTutor.Domain.Authorization;
using FdeTutor.Domain.Events;
using Microsoft.Extensions.DependencyInjection;

namespace FdeTutor.Api.Tests;

public sealed class S083ApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Revision = "032601ea05b48ed716e72ac217a0024ec6ae413b0b27113c704ba6ab4f332522";
    private readonly HttpClient client = CreateClient(factory);

    [Fact]
    public async Task AnonymousRequestsAreRejected()
    {
        using var anonymous = factory.CreateClient();
        var response = await anonymous.GetAsync(
            "/api/v1/s083/content",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CriterionReportsTheEarliestMissingLearningAct()
    {
        var started = await PostAsync<StartSessionRequest, CommandAcceptedResponse>(
            "/api/v1/s083/sessions",
            new StartSessionRequest(Revision));
        var sessionId = started.Event.SessionId;

        var criterion = await client.GetAsync(
            $"/api/v1/s083/sessions/{sessionId}/criterion",
            CancellationToken.None);
        var source = await client.GetAsync(
            $"/api/v1/s083/sessions/{sessionId}/source",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, criterion.StatusCode);
        var error = await criterion.Content.ReadFromJsonAsync<ApiError>(
            CancellationToken.None);
        var sourceError = await source.Content.ReadFromJsonAsync<ApiError>(
            CancellationToken.None);
        Assert.Equal("EXPECTATION_REQUIRED", error?.Code);
        Assert.Equal(HttpStatusCode.Conflict, source.StatusCode);
        Assert.Equal("EXPECTATION_REQUIRED", sourceError?.Code);
        Assert.Equal("EXPECTATION_REQUIRED", sourceError?.RequiredAction);
    }

    [Fact]
    public async Task CompleteAttemptChainUnlocksCriterionAndRetainsNoMasteryEffect()
    {
        var started = await PostAsync<StartSessionRequest, CommandAcceptedResponse>(
            "/api/v1/s083/sessions",
            new StartSessionRequest(Revision));
        var sessionId = started.Event.SessionId;

        await PostAsync<TextSubmissionRequest, CommandAcceptedResponse>(
            $"/api/v1/s083/sessions/{sessionId}/expectation",
            new TextSubmissionRequest(Revision, "Design reinforcement that survives departure."));
        await PostAsync<NamedResponsesRequest, CommandAcceptedResponse>(
            $"/api/v1/s083/sessions/{sessionId}/cold-start",
            new NamedResponsesRequest(
                Revision,
                new Dictionary<string, string>
                {
                    ["S009"] = "The opportunity requires embedded learning and transfer.",
                    ["S064"] = "Deploy, verify, then commit.",
                    ["S078"] = "Make the operating obligation visible.",
                    ["S082"] = "Adoption survives supplier departure.",
                }));
        await PostAsync<NamedResponsesRequest, CommandAcceptedResponse>(
            $"/api/v1/s083/sessions/{sessionId}/priming",
            new NamedResponsesRequest(
                Revision,
                new Dictionary<string, string>
                {
                    ["prime-1"] = "The sponsor stops asking.",
                    ["prime-2"] = "A customer-owned review and artifact.",
                    ["prime-3"] = "The workload shape has changed.",
                }));
        var criterionBeforeRemedy = await client.GetAsync(
            $"/api/v1/s083/sessions/{sessionId}/criterion",
            CancellationToken.None);
        var remedyRequired = await criterionBeforeRemedy.Content.ReadFromJsonAsync<ApiError>(
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.Conflict, criterionBeforeRemedy.StatusCode);
        Assert.Equal("UNPAID_REMEDY_REQUIRED", remedyRequired?.Code);

        var remedy = await PostAsync<TextSubmissionRequest, CommandAcceptedResponse>(
            $"/api/v1/s083/sessions/{sessionId}/unpaid-remedy",
            new TextSubmissionRequest(
                Revision,
                "The customer owner runs a monthly review from an automatically produced drift register when an input crosses its threshold."));

        Assert.Equal(S083LearningState.SourceAvailable, remedy.Policy.State);
        Assert.Equal("NONE", remedy.Policy.MasteryEffect);
        Assert.False(remedy.Policy.EvidenceBearing);

        var criterionBeforeSource = await client.GetAsync(
            $"/api/v1/s083/sessions/{sessionId}/criterion",
            CancellationToken.None);
        var sourceRequired = await criterionBeforeSource.Content.ReadFromJsonAsync<ApiError>(
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.Conflict, criterionBeforeSource.StatusCode);
        Assert.Equal("SOURCE_VIEW_REQUIRED", sourceRequired?.Code);

        var sourceKey = $"test-{Guid.NewGuid():N}";
        var source = await PostAsync<RevisionRequest, SourceOpenResponse>(
            $"/api/v1/s083/sessions/{sessionId}/source/open",
            new RevisionRequest(Revision),
            sourceKey);
        Assert.Contains("Name the condition that can fail", source.SourceHtml);
        Assert.Equal(S083LearningState.CriterionAvailable, source.Command.Policy.State);

        var criterionBeforeReveal = await client.GetAsync(
            $"/api/v1/s083/sessions/{sessionId}/criterion",
            CancellationToken.None);
        var revealRequired = await criterionBeforeReveal.Content.ReadFromJsonAsync<ApiError>(
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.Conflict, criterionBeforeReveal.StatusCode);
        Assert.Equal("CRITERION_REVEAL_REQUIRED", revealRequired?.Code);

        var revealKey = $"test-{Guid.NewGuid():N}";
        var reveal = await PostAsync<RevisionRequest, CriterionRevealResponse>(
            $"/api/v1/s083/sessions/{sessionId}/criterion/reveal",
            new RevisionRequest(Revision),
            revealKey);

        Assert.Equal(4, reveal.Criterion.Elements.Count);
        Assert.Equal(S083LearningState.ComparisonRequired, reveal.Command.Policy.State);

        await PostAsync<TextSubmissionRequest, CommandAcceptedResponse>(
            $"/api/v1/s083/sessions/{sessionId}/comparison",
            new TextSubmissionRequest(
                Revision,
                "The first answer needed clearer authority and an unattended artifact."));
        await PostAsync<TextSubmissionRequest, CommandAcceptedResponse>(
            $"/api/v1/s083/sessions/{sessionId}/revision",
            new TextSubmissionRequest(
                Revision,
                "A customer owner with authority runs a durable cadence from an automatically produced artifact and observable trigger."));
        var transfer = await PostAsync<TextSubmissionRequest, CommandAcceptedResponse>(
            $"/api/v1/s083/sessions/{sessionId}/authentic-transfer",
            new TextSubmissionRequest(
                Revision,
                "In a synthetic drift register, the original workload assumption, moved input, owner, first visible sign, and fallback are explicit."));
        Assert.Equal(
            "AUTHENTIC_WORK",
            transfer.Event.Payload.GetProperty("classification").GetString());
        Assert.Equal(
            "SYNTHETIC_REDACTED_OR_EXPLICITLY_APPROVED",
            transfer.Event.Payload.GetProperty("pilotRestriction").GetString());
        var requestedDueAt = DateTimeOffset.UtcNow
            .AddSeconds(2)
            .ToOffset(TimeSpan.FromHours(2));
        var scheduleKey = $"test-{Guid.NewGuid():N}";
        var scheduled = await PostAsync<RetrievalScheduleRequest, CommandAcceptedResponse>(
            $"/api/v1/s083/sessions/{sessionId}/retrieval-schedule",
            new RetrievalScheduleRequest(Revision, requestedDueAt),
            scheduleKey);
        Assert.Equal(
            TimeSpan.Zero,
            scheduled.Event.Payload.GetProperty("dueAt").GetDateTimeOffset().Offset);

        var newerSession = await PostAsync<StartSessionRequest, CommandAcceptedResponse>(
            "/api/v1/s083/sessions",
            new StartSessionRequest(Revision));
        var newerSessionId = newerSession.Event.SessionId;
        await PostAsync<TextSubmissionRequest, CommandAcceptedResponse>(
            $"/api/v1/s083/sessions/{newerSessionId}/expectation",
            new TextSubmissionRequest(
                Revision,
                "This session remains resumable while the older retrieval is completed."));
        await PostAsync<NamedResponsesRequest, CommandAcceptedResponse>(
            $"/api/v1/s083/sessions/{newerSessionId}/cold-start",
            new NamedResponsesRequest(
                Revision,
                new Dictionary<string, string>
                {
                    ["S009"] = "Embedded learning and transfer.",
                    ["S064"] = "Deploy, verify, then commit.",
                    ["S078"] = "Record the operating obligation.",
                    ["S082"] = "Adoption survives departure.",
                }));
        await PostAsync<NamedResponsesRequest, CommandAcceptedResponse>(
            $"/api/v1/s083/sessions/{newerSessionId}/priming",
            new NamedResponsesRequest(
                Revision,
                new Dictionary<string, string>
                {
                    ["prime-1"] = "Attention may disappear.",
                    ["prime-2"] = "The customer-owned version contains the mechanism.",
                    ["prime-3"] = "The workload assumption may have moved.",
                }));
        await PostAsync<TextSubmissionRequest, CommandAcceptedResponse>(
            $"/api/v1/s083/sessions/{newerSessionId}/unpaid-remedy",
            new TextSubmissionRequest(
                Revision,
                "A customer owner runs the cadence from an automatically produced artifact and state-change trigger."));
        await PostAsync<RevisionRequest, SourceOpenResponse>(
            $"/api/v1/s083/sessions/{newerSessionId}/source/open",
            new RevisionRequest(Revision));
        await PostAsync<RevisionRequest, CriterionRevealResponse>(
            $"/api/v1/s083/sessions/{newerSessionId}/criterion/reveal",
            new RevisionRequest(Revision));

        await Task.Delay(2200, CancellationToken.None);

        var dueState = await client.GetFromJsonAsync<S083StateResponse>(
            $"/api/v1/s083/sessions/{sessionId}",
            CancellationToken.None);
        var sourceDuringRecall = await client.GetAsync(
            $"/api/v1/s083/sessions/{sessionId}/source",
            CancellationToken.None);
        var criterionDuringRecall = await client.GetAsync(
            $"/api/v1/s083/sessions/{sessionId}/criterion",
            CancellationToken.None);
        var otherSourceDuringRecall = await client.GetAsync(
            $"/api/v1/s083/sessions/{newerSessionId}/source",
            CancellationToken.None);
        var otherCriterionDuringRecall = await client.GetAsync(
            $"/api/v1/s083/sessions/{newerSessionId}/criterion",
            CancellationToken.None);
        var otherStateDuringRecall = await client.GetFromJsonAsync<S083StateResponse>(
            $"/api/v1/s083/sessions/{newerSessionId}",
            CancellationToken.None);
        var dueSafeContent = await client.GetFromJsonAsync<S083ContentResponse>(
            "/api/v1/s083/content",
            CancellationToken.None);

        Assert.Equal(S083LearningState.RetrievalDue, dueState?.Policy.State);
        Assert.False(dueState!.Policy.CriterionRevealAllowed);
        Assert.Equal(
            "[REDACTED_DURING_SOURCE_ABSENT_RECALL]",
            dueState.Timeline
                .Single(item => item.EventType == LearnerEventTypes.UnpaidRemedyRecorded)
                .Payload.GetProperty("response")
                .GetString());
        var redactedTransfer = dueState.Timeline.Single(
            item => item.EventType == LearnerEventTypes.ArtifactSubmitted);
        Assert.Equal(
            "[REDACTED_DURING_SOURCE_ABSENT_RECALL]",
            redactedTransfer.Payload.GetProperty("response").GetString());
        Assert.Equal(
            "AUTHENTIC_WORK",
            redactedTransfer.Payload.GetProperty("classification").GetString());
        Assert.Equal(
            "SYNTHETIC_REDACTED_OR_EXPLICITLY_APPROVED",
            redactedTransfer.Payload.GetProperty("pilotRestriction").GetString());
        Assert.Equal(
            "[REDACTED_DURING_SOURCE_ABSENT_RECALL]",
            otherStateDuringRecall!.Timeline
                .Single(item => item.EventType == LearnerEventTypes.UnpaidRemedyRecorded)
                .Payload.GetProperty("response")
                .GetString());
        Assert.Empty(otherStateDuringRecall.Policy.PermittedActions);
        Assert.False(otherStateDuringRecall.Policy.CriterionRevealAllowed);
        Assert.Equal(
            PolicyDenialReason.DueRetrievalRequired,
            otherStateDuringRecall.Policy.DenialReason);
        Assert.True(dueSafeContent?.SourceAbsentRecall);
        Assert.Empty(dueSafeContent!.Vocabulary);
        Assert.Empty(dueSafeContent.ColdStartPrompts);
        Assert.Equal(HttpStatusCode.Conflict, sourceDuringRecall.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, criterionDuringRecall.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, otherSourceDuringRecall.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, otherCriterionDuringRecall.StatusCode);
        var scheduleReplay = await PostAsync<
            RetrievalScheduleRequest,
            CommandAcceptedResponse>(
            $"/api/v1/s083/sessions/{sessionId}/retrieval-schedule",
            new RetrievalScheduleRequest(Revision, requestedDueAt),
            scheduleKey);
        Assert.True(scheduleReplay.IsDuplicate);
        Assert.Equal(
            S083LearningState.RetrievalScheduled,
            scheduleReplay.Policy.State);
        using var replaySource = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/s083/sessions/{sessionId}/source/open")
        {
            Content = JsonContent.Create(new RevisionRequest(Revision)),
        };
        replaySource.Headers.Add("Idempotency-Key", sourceKey);
        using var replayCriterion = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/s083/sessions/{sessionId}/criterion/reveal")
        {
            Content = JsonContent.Create(new RevisionRequest(Revision)),
        };
        replayCriterion.Headers.Add("Idempotency-Key", revealKey);
        var replaySourceResponse = await client.SendAsync(
            replaySource,
            CancellationToken.None);
        var replayCriterionResponse = await client.SendAsync(
            replayCriterion,
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.Conflict, replaySourceResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, replayCriterionResponse.StatusCode);

        await PostAsync<TextSubmissionRequest, CommandAcceptedResponse>(
            $"/api/v1/s083/sessions/{sessionId}/retrieval",
            new TextSubmissionRequest(
                Revision,
                "The mechanism survives when owner, cadence, artifact, and trigger remain customer-owned."));
        var home = await client.GetFromJsonAsync<LearningHomeResponse>(
            "/api/v1/s083/learning-home",
            CancellationToken.None);

        Assert.Equal(newerSessionId, home?.Current?.SessionId);
    }

    [Fact]
    public async Task SameIdempotencyKeyDoesNotAppendTwice()
    {
        var request = new StartSessionRequest(Revision);
        var key = $"test-{Guid.NewGuid():N}";
        var first = await PostAsync<StartSessionRequest, CommandAcceptedResponse>(
            "/api/v1/s083/sessions",
            request,
            key);
        var second = await PostAsync<StartSessionRequest, CommandAcceptedResponse>(
            "/api/v1/s083/sessions",
            request,
            key);

        Assert.False(first.IsDuplicate);
        Assert.True(second.IsDuplicate);
        Assert.Equal(first.Event.EventId, second.Event.EventId);
        Assert.Equal(first.Event.SessionId, second.Event.SessionId);
    }

    [Fact]
    public async Task SuccessfulCommandRetryReturnsItsOriginalResultAfterStateAdvanced()
    {
        var started = await PostAsync<StartSessionRequest, CommandAcceptedResponse>(
            "/api/v1/s083/sessions",
            new StartSessionRequest(Revision));
        var key = $"test-{Guid.NewGuid():N}";
        var request = new TextSubmissionRequest(
            Revision,
            "I expect to design a customer-owned reinforcement mechanism.");
        var first = await PostAsync<TextSubmissionRequest, CommandAcceptedResponse>(
            $"/api/v1/s083/sessions/{started.Event.SessionId}/expectation",
            request,
            key);
        var retry = await PostAsync<TextSubmissionRequest, CommandAcceptedResponse>(
            $"/api/v1/s083/sessions/{started.Event.SessionId}/expectation",
            request,
            key);

        Assert.False(first.IsDuplicate);
        Assert.True(retry.IsDuplicate);
        Assert.Equal(first.Event.EventId, retry.Event.EventId);
        Assert.Equal(S083LearningState.ColdStartRequired, retry.Policy.State);
    }

    [Fact]
    public async Task IdempotencyKeyCannotLeakAnEventAcrossLearners()
    {
        var key = $"test-{Guid.NewGuid():N}";
        var owner = await PostAsync<StartSessionRequest, CommandAcceptedResponse>(
            "/api/v1/s083/sessions",
            new StartSessionRequest(Revision),
            key);
        using var otherLearner = factory.CreateClient();
        otherLearner.DefaultRequestHeaders.Add(
            "X-Fde-Tenant-Id",
            "11111111-1111-1111-1111-111111111111");
        otherLearner.DefaultRequestHeaders.Add(
            "X-Fde-Object-Id",
            "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/s083/sessions")
        {
            Content = JsonContent.Create(new StartSessionRequest(Revision)),
        };
        message.Headers.Add("Idempotency-Key", key);

        var response = await otherLearner.SendAsync(message, CancellationToken.None);
        var error = await response.Content.ReadFromJsonAsync<ApiError>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("IDEMPOTENCY_KEY_CONFLICT", error?.Code);
        Assert.DoesNotContain(owner.Event.EventId.ToString(), await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SessionRejectsACommandUsingAnotherContentRevision()
    {
        var started = await PostAsync<StartSessionRequest, CommandAcceptedResponse>(
            "/api/v1/s083/sessions",
            new StartSessionRequest(Revision));
        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/s083/sessions/{started.Event.SessionId}/expectation")
        {
            Content = JsonContent.Create(new TextSubmissionRequest(
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                "This command must not change the pinned session revision.")),
        };
        message.Headers.Add("Idempotency-Key", $"test-{Guid.NewGuid():N}");
        var response = await client.SendAsync(message, CancellationToken.None);
        var error = await response.Content.ReadFromJsonAsync<ApiError>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("SESSION_REVISION_MISMATCH", error?.Code);
    }

    [Fact]
    public async Task ColdStartRequiresTheExactPromptSet()
    {
        var started = await PostAsync<StartSessionRequest, CommandAcceptedResponse>(
            "/api/v1/s083/sessions",
            new StartSessionRequest(Revision));
        await PostAsync<TextSubmissionRequest, CommandAcceptedResponse>(
            $"/api/v1/s083/sessions/{started.Event.SessionId}/expectation",
            new TextSubmissionRequest(Revision, "I expect to identify durable reinforcement."));
        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/s083/sessions/{started.Event.SessionId}/cold-start")
        {
            Content = JsonContent.Create(new NamedResponsesRequest(
                Revision,
                new Dictionary<string, string>
                {
                    ["not-a-real-prompt"] = "This must not advance the state.",
                })),
        };
        message.Headers.Add("Idempotency-Key", $"test-{Guid.NewGuid():N}");
        var response = await client.SendAsync(message, CancellationToken.None);
        var error = await response.Content.ReadFromJsonAsync<ApiError>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("INVALID_RESPONSE_SET", error?.Code);
    }

    [Fact]
    public async Task NullNamedResponsesReturnTypedBadRequest()
    {
        var started = await PostAsync<StartSessionRequest, CommandAcceptedResponse>(
            "/api/v1/s083/sessions",
            new StartSessionRequest(Revision));
        await PostAsync<TextSubmissionRequest, CommandAcceptedResponse>(
            $"/api/v1/s083/sessions/{started.Event.SessionId}/expectation",
            new TextSubmissionRequest(Revision, "I expect to identify durable reinforcement."));
        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/s083/sessions/{started.Event.SessionId}/cold-start")
        {
            Content = JsonContent.Create(new NamedResponsesRequest(Revision, null!)),
        };
        message.Headers.Add("Idempotency-Key", $"test-{Guid.NewGuid():N}");

        var response = await client.SendAsync(message, CancellationToken.None);
        var error = await response.Content.ReadFromJsonAsync<ApiError>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("INVALID_RESPONSE", error?.Code);
    }

    [Fact]
    public async Task MalformedCanonicalScheduleDoesNotCrashLearningHome()
    {
        var started = await PostAsync<StartSessionRequest, CommandAcceptedResponse>(
            "/api/v1/s083/sessions",
            new StartSessionRequest(Revision));
        var store = factory.Services.GetRequiredService<ILearnerEventStore>();
        var authorization = new LearnerAuthorizationContext(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "test-subject",
            new HashSet<string>(StringComparer.Ordinal) { "Learner" });
        await store.AppendAsync(
            new AppendLearnerEventCommand(
                authorization,
                started.Event.SessionId,
                LearnerEventTypes.RetrievalScheduled,
                "S083",
                Revision,
                1,
                Guid.NewGuid(),
                started.Event.EventId,
                $"test-{Guid.NewGuid():N}",
                DateTimeOffset.UtcNow,
                System.Text.Json.JsonSerializer.SerializeToElement(new
                {
                    dueAt = 42,
                    mode = "CHANGED_CONTEXT_SAME_NODE",
                })),
            CancellationToken.None);

        var response = await client.GetAsync(
            "/api/v1/s083/learning-home",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ConcurrentTransitionsCannotAppendTheSameStreamVersion()
    {
        var started = await PostAsync<StartSessionRequest, CommandAcceptedResponse>(
            "/api/v1/s083/sessions",
            new StartSessionRequest(Revision));
        HttpRequestMessage CreateExpectation(string response)
        {
            var message = new HttpRequestMessage(
                HttpMethod.Post,
                $"/api/v1/s083/sessions/{started.Event.SessionId}/expectation")
            {
                Content = JsonContent.Create(new TextSubmissionRequest(Revision, response)),
            };
            message.Headers.Add("Idempotency-Key", $"test-{Guid.NewGuid():N}");
            return message;
        }

        using var first = CreateExpectation("First concurrent expectation.");
        using var second = CreateExpectation("Second concurrent expectation.");
        var responses = await Task.WhenAll(
            client.SendAsync(first, CancellationToken.None),
            client.SendAsync(second, CancellationToken.None));
        var state = await client.GetFromJsonAsync<S083StateResponse>(
            $"/api/v1/s083/sessions/{started.Event.SessionId}",
            CancellationToken.None);

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        Assert.Single(
            state!.Timeline,
            item => item.EventType == LearnerEventTypes.ExpectationRecorded);
    }

    [Fact]
    public async Task RevisionMismatchReturnsTypedConflict()
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/s083/sessions")
        {
            Content = JsonContent.Create(new StartSessionRequest("a".PadLeft(64, 'a'))),
        };
        message.Headers.Add("Idempotency-Key", $"test-{Guid.NewGuid():N}");
        var response = await client.SendAsync(message, CancellationToken.None);
        var error = await response.Content.ReadFromJsonAsync<ApiError>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("REVISION_MISMATCH", error?.Code);
    }

    [Fact]
    public async Task AnotherLearnerCannotReadTheSession()
    {
        var started = await PostAsync<StartSessionRequest, CommandAcceptedResponse>(
            "/api/v1/s083/sessions",
            new StartSessionRequest(Revision));
        using var otherLearner = factory.CreateClient();
        otherLearner.DefaultRequestHeaders.Add(
            "X-Fde-Tenant-Id",
            "11111111-1111-1111-1111-111111111111");
        otherLearner.DefaultRequestHeaders.Add(
            "X-Fde-Object-Id",
            "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        var response = await otherLearner.GetAsync(
            $"/api/v1/s083/sessions/{started.Event.SessionId}",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PolicyStateUsesAStringWireValue()
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/s083/sessions")
        {
            Content = JsonContent.Create(new StartSessionRequest(Revision)),
        };
        message.Headers.Add("Idempotency-Key", $"test-{Guid.NewGuid():N}");
        var response = await client.SendAsync(message, CancellationToken.None);
        response.EnsureSuccessStatusCode();
        using var document = await System.Text.Json.JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(CancellationToken.None),
            cancellationToken: CancellationToken.None);

        var state = document.RootElement.GetProperty("policy").GetProperty("state");
        Assert.Equal(System.Text.Json.JsonValueKind.String, state.ValueKind);
        Assert.Equal("ExpectationRequired", state.GetString());
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(
        string path,
        TRequest request,
        string? idempotencyKey = null)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(request),
        };
        message.Headers.Add("Idempotency-Key", idempotencyKey ?? $"test-{Guid.NewGuid():N}");
        message.Headers.Add("X-Correlation-Id", Guid.NewGuid().ToString());
        var response = await client.SendAsync(
            message,
            CancellationToken.None);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TResponse>(
            CancellationToken.None))!;
    }

    private static HttpClient CreateClient(ApiFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(
            "X-Fde-Tenant-Id",
            "11111111-1111-1111-1111-111111111111");
        client.DefaultRequestHeaders.Add(
            "X-Fde-Object-Id",
            "22222222-2222-2222-2222-222222222222");
        return client;
    }
}
