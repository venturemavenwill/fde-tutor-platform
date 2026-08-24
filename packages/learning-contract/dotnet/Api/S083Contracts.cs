using FdeTutor.Contracts.Events;
using FdeTutor.Contracts.Policy;

namespace FdeTutor.Contracts.Api;

public sealed record StartSessionRequest(string ContentRevision);

public sealed record RevisionRequest(string ContentRevision);

public sealed record TextSubmissionRequest(string ContentRevision, string Response);

public sealed record NamedResponsesRequest(
    string ContentRevision,
    IReadOnlyDictionary<string, string> Responses);

public sealed record RetrievalScheduleRequest(
    string ContentRevision,
    DateTimeOffset DueAt);

public sealed record CriterionResponse(
    string ContentNodeId,
    string ContentRevision,
    IReadOnlyList<string> Elements);

public sealed record CriterionRevealResponse(
    CommandAcceptedResponse Command,
    CriterionResponse Criterion);

public sealed record SourceOpenResponse(
    CommandAcceptedResponse Command,
    string SourceHtml);

public sealed record VocabularyItemResponse(string Term, string Definition);

public sealed record PromptResponse(string Id, string Prompt);

public sealed record AuthenticTransferContractResponse(
    string Prompt,
    string ArtifactClassification,
    string PilotRestriction);

public sealed record S083ContentResponse(
    string ContentNodeId,
    string ContentRevision,
    bool SourceAbsentRecall,
    string Title,
    int ExpectedDurationMinutes,
    string Organizer,
    IReadOnlyList<VocabularyItemResponse> Vocabulary,
    string ExpectationPrompt,
    IReadOnlyList<PromptResponse> ColdStartPrompts,
    IReadOnlyList<PromptResponse> PrimingPrompts,
    string UnpaidRemedyPrompt,
    string ComparisonPrompt,
    AuthenticTransferContractResponse AuthenticTransfer,
    bool AssessmentBearing,
    string MasteryEffect);

public sealed record S083StateResponse(
    Guid? SessionId,
    string ContentNodeId,
    string ContentRevision,
    S083PolicyDecision Policy,
    IReadOnlyList<LearnerEventEnvelope> Timeline,
    long ProjectionVersion);

public sealed record CommandAcceptedResponse(
    LearnerEventEnvelope Event,
    bool IsDuplicate,
    S083PolicyDecision Policy);

public sealed record LearningHomeResponse(
    S083StateResponse? Current,
    IReadOnlyList<DueRetrievalResponse> DueRetrievals);

public sealed record DueRetrievalResponse(
    Guid SessionId,
    string ContentNodeId,
    DateTimeOffset DueAt,
    bool IsDue);

public sealed record ApiError(
    string Code,
    string Message,
    string? RequiredAction = null);
