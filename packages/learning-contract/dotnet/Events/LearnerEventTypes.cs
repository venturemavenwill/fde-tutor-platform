namespace FdeTutor.Contracts.Events;

public static class LearnerEventTypes
{
    public const string LearningSessionStarted = nameof(LearningSessionStarted);
    public const string LearningSessionEnded = nameof(LearningSessionEnded);
    public const string NodeOpened = nameof(NodeOpened);
    public const string ExpectationRecorded = nameof(ExpectationRecorded);
    public const string PrerequisiteRecallAttempted = nameof(PrerequisiteRecallAttempted);
    public const string PrimingResponseSubmitted = nameof(PrimingResponseSubmitted);
    public const string UnpaidRemedyRecorded = nameof(UnpaidRemedyRecorded);
    public const string SourceViewed = nameof(SourceViewed);
    public const string ModelAnswerRevealed = nameof(ModelAnswerRevealed);
    public const string ComparisonRecorded = nameof(ComparisonRecorded);
    public const string ProposalRevisionRecorded = nameof(ProposalRevisionRecorded);
    public const string RetrievalScheduled = nameof(RetrievalScheduled);
    public const string RetrievalBecameDue = nameof(RetrievalBecameDue);
    public const string RetrievalCompleted = nameof(RetrievalCompleted);
    public const string RecommendationIssued = nameof(RecommendationIssued);
    public const string ArtifactSubmitted = nameof(ArtifactSubmitted);
    public const string EvidenceEvaluationProposed = nameof(EvidenceEvaluationProposed);
    public const string EvidenceEvaluationConfirmed = nameof(EvidenceEvaluationConfirmed);
    public const string EvidenceEvaluationOverridden = nameof(EvidenceEvaluationOverridden);
    public const string EvaluationDisputed = nameof(EvaluationDisputed);
    public const string InstructorFeedbackAdded = nameof(InstructorFeedbackAdded);
    public const string EventCorrectionRecorded = nameof(EventCorrectionRecorded);
}
