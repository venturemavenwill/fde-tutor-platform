using FdeTutor.Contracts.Policy;

namespace FdeTutor.Api.Learning;

public static class PolicyErrorCodes
{
    public static string From(PolicyDenialReason reason) =>
        reason switch
        {
            PolicyDenialReason.None => "NONE",
            PolicyDenialReason.SessionRequired => "SESSION_REQUIRED",
            PolicyDenialReason.ExpectationRequired => "EXPECTATION_REQUIRED",
            PolicyDenialReason.ColdStartRequired => "COLD_START_REQUIRED",
            PolicyDenialReason.PrimingRequired => "PRIMING_REQUIRED",
            PolicyDenialReason.UnpaidRemedyRequired => "UNPAID_REMEDY_REQUIRED",
            PolicyDenialReason.SourceViewRequired => "SOURCE_VIEW_REQUIRED",
            PolicyDenialReason.CriterionRevealRequired => "CRITERION_REVEAL_REQUIRED",
            PolicyDenialReason.ComparisonRequired => "COMPARISON_REQUIRED",
            PolicyDenialReason.RevisionRequired => "REVISION_REQUIRED",
            PolicyDenialReason.AuthenticTransferRequired => "AUTHENTIC_TRANSFER_REQUIRED",
            PolicyDenialReason.RetrievalScheduleRequired => "RETRIEVAL_SCHEDULE_REQUIRED",
            PolicyDenialReason.RetrievalNotDue => "RETRIEVAL_NOT_DUE",
            PolicyDenialReason.DueRetrievalRequired => "DUE_RETRIEVAL_REQUIRED",
            PolicyDenialReason.GroundingRequired => "GROUNDING_REQUIRED",
            PolicyDenialReason.RevisionMismatch => "REVISION_MISMATCH",
            PolicyDenialReason.ActionAlreadyCompleted => "ACTION_ALREADY_COMPLETED",
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
        };
}
