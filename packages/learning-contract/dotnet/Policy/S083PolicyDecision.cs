using System.Text.Json.Serialization;

namespace FdeTutor.Contracts.Policy;

[JsonConverter(typeof(JsonStringEnumConverter<S083LearningState>))]
public enum S083LearningState
{
    Orient,
    ExpectationRequired,
    ColdStartRequired,
    PrimingRequired,
    UnpaidRemedyRequired,
    SourceAvailable,
    CriterionAvailable,
    ComparisonRequired,
    RevisionAvailable,
    AuthenticTransferRequired,
    RetrievalScheduleRequired,
    RetrievalScheduled,
    RetrievalDue,
    Complete,
}

[JsonConverter(typeof(JsonStringEnumConverter<S083Action>))]
public enum S083Action
{
    ViewOrganizer,
    RecordExpectation,
    AttemptColdStart,
    AnswerPriming,
    RecordUnpaidRemedy,
    ViewSource,
    RevealCriterion,
    RecordComparison,
    RecordRevision,
    RecordAuthenticTransfer,
    ScheduleRetrieval,
    CompleteRetrieval,
}

[JsonConverter(typeof(JsonStringEnumConverter<PolicyDenialReason>))]
public enum PolicyDenialReason
{
    None,
    SessionRequired,
    ExpectationRequired,
    ColdStartRequired,
    PrimingRequired,
    UnpaidRemedyRequired,
    SourceViewRequired,
    CriterionRevealRequired,
    ComparisonRequired,
    RevisionRequired,
    AuthenticTransferRequired,
    RetrievalScheduleRequired,
    RetrievalNotDue,
    DueRetrievalRequired,
    GroundingRequired,
    RevisionMismatch,
    ActionAlreadyCompleted,
}

public sealed record S083PolicyDecision(
    string SchemaVersion,
    string PolicyVersion,
    string ContentNodeId,
    S083LearningState State,
    IReadOnlyCollection<S083Action> PermittedActions,
    bool CriterionRevealAllowed,
    bool PaidProposalImprovementAllowed,
    bool EvidenceBearing,
    string MasteryEffect,
    bool HumanReviewRequired,
    string GroundingResult,
    PolicyDenialReason DenialReason = PolicyDenialReason.None);
