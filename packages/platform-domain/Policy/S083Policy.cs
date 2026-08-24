using FdeTutor.Contracts.Events;
using FdeTutor.Contracts.Policy;

namespace FdeTutor.Domain.Policy;

public static class S083Policy
{
    public const string PolicyVersion = "s083-policy-1";
    public const string ContentNodeId = "S083";

    public static S083PolicyDecision Evaluate(
        IEnumerable<LearnerEventEnvelope> events,
        DateTimeOffset? now = null)
    {
        var timeline = events.OrderBy(item => item.RecordedAt).ThenBy(item => item.EventId).ToArray();

        if (!Has(timeline, LearnerEventTypes.LearningSessionStarted))
        {
            return Decision(
                S083LearningState.Orient,
                [S083Action.ViewOrganizer],
                PolicyDenialReason.SessionRequired);
        }

        if (!Has(timeline, LearnerEventTypes.ExpectationRecorded))
        {
            return Decision(
                S083LearningState.ExpectationRequired,
                [S083Action.ViewOrganizer, S083Action.RecordExpectation],
                PolicyDenialReason.ExpectationRequired);
        }

        if (!Has(timeline, LearnerEventTypes.PrerequisiteRecallAttempted))
        {
            return Decision(
                S083LearningState.ColdStartRequired,
                [S083Action.AttemptColdStart],
                PolicyDenialReason.ColdStartRequired);
        }

        if (!Has(timeline, LearnerEventTypes.PrimingResponseSubmitted))
        {
            return Decision(
                S083LearningState.PrimingRequired,
                [S083Action.AnswerPriming],
                PolicyDenialReason.PrimingRequired);
        }

        if (!Has(timeline, LearnerEventTypes.UnpaidRemedyRecorded))
        {
            return Decision(
                S083LearningState.UnpaidRemedyRequired,
                [S083Action.RecordUnpaidRemedy],
                PolicyDenialReason.UnpaidRemedyRequired);
        }

        if (!Has(timeline, LearnerEventTypes.SourceViewed))
        {
            return Decision(
                S083LearningState.SourceAvailable,
                [S083Action.ViewSource],
                PolicyDenialReason.SourceViewRequired,
                criterionRevealAllowed: false,
                paidProposalImprovementAllowed: true);
        }

        if (!Has(timeline, LearnerEventTypes.ModelAnswerRevealed))
        {
            return Decision(
                S083LearningState.CriterionAvailable,
                [S083Action.ViewSource, S083Action.RevealCriterion],
                criterionRevealAllowed: true,
                paidProposalImprovementAllowed: true);
        }

        if (!Has(timeline, LearnerEventTypes.ComparisonRecorded))
        {
            return Decision(
                S083LearningState.ComparisonRequired,
                [S083Action.RecordComparison],
                PolicyDenialReason.ComparisonRequired,
                criterionRevealAllowed: true,
                paidProposalImprovementAllowed: true);
        }

        if (!Has(timeline, LearnerEventTypes.ProposalRevisionRecorded))
        {
            return Decision(
                S083LearningState.RevisionAvailable,
                [S083Action.RecordRevision],
                PolicyDenialReason.RevisionRequired,
                criterionRevealAllowed: true,
                paidProposalImprovementAllowed: true);
        }

        if (!Has(timeline, LearnerEventTypes.ArtifactSubmitted))
        {
            return Decision(
                S083LearningState.AuthenticTransferRequired,
                [S083Action.RecordAuthenticTransfer],
                PolicyDenialReason.AuthenticTransferRequired,
                criterionRevealAllowed: true,
                paidProposalImprovementAllowed: true);
        }

        var scheduled = timeline.LastOrDefault(item => item.EventType == LearnerEventTypes.RetrievalScheduled);
        if (scheduled is null)
        {
            return Decision(
                S083LearningState.RetrievalScheduleRequired,
                [S083Action.ScheduleRetrieval],
                PolicyDenialReason.RetrievalScheduleRequired,
                criterionRevealAllowed: true,
                paidProposalImprovementAllowed: true);
        }

        if (Has(timeline, LearnerEventTypes.RetrievalCompleted))
        {
            return Decision(
                S083LearningState.Complete,
                [],
                criterionRevealAllowed: true,
                paidProposalImprovementAllowed: true);
        }

        var dueAt = TryReadDueAt(scheduled);
        var effectiveNow = now ?? DateTimeOffset.UtcNow;
        if (dueAt is not null && dueAt <= effectiveNow)
        {
            return Decision(
                S083LearningState.RetrievalDue,
                [S083Action.CompleteRetrieval],
                criterionRevealAllowed: false,
                paidProposalImprovementAllowed: false);
        }

        return Decision(
            S083LearningState.RetrievalScheduled,
            [],
            PolicyDenialReason.RetrievalNotDue,
            criterionRevealAllowed: true,
            paidProposalImprovementAllowed: true);
    }

    public static PolicyDenialReason AuthorizeEvent(
        IEnumerable<LearnerEventEnvelope> events,
        string eventType,
        DateTimeOffset? now = null)
    {
        var decision = Evaluate(events, now);
        var action = EventAction(eventType);
        return action is null || decision.PermittedActions.Contains(action.Value)
            ? PolicyDenialReason.None
            : decision.DenialReason == PolicyDenialReason.None
                ? PolicyDenialReason.ActionAlreadyCompleted
                : decision.DenialReason;
    }

    public static IReadOnlyList<string> ProjectSupportUsed(
        IEnumerable<LearnerEventEnvelope> events)
    {
        var eventTypes = events.Select(item => item.EventType).ToHashSet(StringComparer.Ordinal);
        var support = new List<string>();
        if (eventTypes.Contains(LearnerEventTypes.SourceViewed))
        {
            support.Add("SOURCE");
        }
        if (eventTypes.Contains(LearnerEventTypes.ModelAnswerRevealed))
        {
            support.Add("CRITERION");
        }
        if (eventTypes.Contains(LearnerEventTypes.InstructorFeedbackAdded))
        {
            support.Add("HUMAN_FEEDBACK");
        }
        return support;
    }

    private static S083Action? EventAction(string eventType) =>
        eventType switch
        {
            LearnerEventTypes.LearningSessionStarted => null,
            LearnerEventTypes.ExpectationRecorded => S083Action.RecordExpectation,
            LearnerEventTypes.PrerequisiteRecallAttempted => S083Action.AttemptColdStart,
            LearnerEventTypes.PrimingResponseSubmitted => S083Action.AnswerPriming,
            LearnerEventTypes.UnpaidRemedyRecorded => S083Action.RecordUnpaidRemedy,
            LearnerEventTypes.SourceViewed => S083Action.ViewSource,
            LearnerEventTypes.ModelAnswerRevealed => S083Action.RevealCriterion,
            LearnerEventTypes.ComparisonRecorded => S083Action.RecordComparison,
            LearnerEventTypes.ProposalRevisionRecorded => S083Action.RecordRevision,
            LearnerEventTypes.ArtifactSubmitted => S083Action.RecordAuthenticTransfer,
            LearnerEventTypes.RetrievalScheduled => S083Action.ScheduleRetrieval,
            LearnerEventTypes.RetrievalCompleted => S083Action.CompleteRetrieval,
            _ => null,
        };

    private static bool Has(
        IEnumerable<LearnerEventEnvelope> events,
        string eventType) =>
        events.Any(item => item.EventType == eventType);

    private static DateTimeOffset? TryReadDueAt(LearnerEventEnvelope scheduled)
    {
        if (!scheduled.Payload.TryGetProperty("dueAt", out var dueAtProperty))
        {
            return null;
        }

        return dueAtProperty.ValueKind == System.Text.Json.JsonValueKind.String &&
               DateTimeOffset.TryParse(dueAtProperty.GetString(), out var dueAt)
            ? dueAt
            : null;
    }

    private static S083PolicyDecision Decision(
        S083LearningState state,
        IReadOnlyCollection<S083Action> permittedActions,
        PolicyDenialReason denialReason = PolicyDenialReason.None,
        bool criterionRevealAllowed = false,
        bool paidProposalImprovementAllowed = false) =>
        new(
            "1.0.0",
            PolicyVersion,
            ContentNodeId,
            state,
            permittedActions,
            criterionRevealAllowed,
            paidProposalImprovementAllowed,
            EvidenceBearing: false,
            MasteryEffect: "NONE",
            HumanReviewRequired: false,
            GroundingResult: "NOT_APPLICABLE",
            denialReason);
}
