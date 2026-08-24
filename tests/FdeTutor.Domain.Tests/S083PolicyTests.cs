using System.Text.Json;
using FdeTutor.Contracts.Events;
using FdeTutor.Contracts.Policy;
using FdeTutor.Domain.Policy;

namespace FdeTutor.Domain.Tests;

public sealed class S083PolicyTests
{
    private const string Revision = "032601ea05b48ed716e72ac217a0024ec6ae413b0b27113c704ba6ab4f332522";

    [Fact]
    public void CriterionAndPaidImprovementRemainLockedBeforeUnpaidRemedy()
    {
        var events = Timeline(
            LearnerEventTypes.LearningSessionStarted,
            LearnerEventTypes.ExpectationRecorded,
            LearnerEventTypes.PrerequisiteRecallAttempted,
            LearnerEventTypes.PrimingResponseSubmitted);

        var decision = S083Policy.Evaluate(events);

        Assert.Equal(S083LearningState.UnpaidRemedyRequired, decision.State);
        Assert.False(decision.CriterionRevealAllowed);
        Assert.False(decision.PaidProposalImprovementAllowed);
        Assert.Equal("NONE", decision.MasteryEffect);
        Assert.False(decision.EvidenceBearing);
        Assert.Equal(
            PolicyDenialReason.UnpaidRemedyRequired,
            S083Policy.AuthorizeEvent(events, LearnerEventTypes.ModelAnswerRevealed));
    }

    [Fact]
    public void UnpaidRemedyUnlocksSourceBeforeCriterion()
    {
        var events = Timeline(
            LearnerEventTypes.LearningSessionStarted,
            LearnerEventTypes.ExpectationRecorded,
            LearnerEventTypes.PrerequisiteRecallAttempted,
            LearnerEventTypes.PrimingResponseSubmitted,
            LearnerEventTypes.UnpaidRemedyRecorded);

        var decision = S083Policy.Evaluate(events);

        Assert.Equal(S083LearningState.SourceAvailable, decision.State);
        Assert.False(decision.CriterionRevealAllowed);
        Assert.True(decision.PaidProposalImprovementAllowed);
        Assert.Equal(
            PolicyDenialReason.SourceViewRequired,
            S083Policy.AuthorizeEvent(events, LearnerEventTypes.ModelAnswerRevealed));
        Assert.Equal(
            PolicyDenialReason.None,
            S083Policy.AuthorizeEvent(events, LearnerEventTypes.SourceViewed));
    }

    [Fact]
    public void SourceViewUnlocksCriterionReveal()
    {
        var events = Timeline(
            LearnerEventTypes.LearningSessionStarted,
            LearnerEventTypes.ExpectationRecorded,
            LearnerEventTypes.PrerequisiteRecallAttempted,
            LearnerEventTypes.PrimingResponseSubmitted,
            LearnerEventTypes.UnpaidRemedyRecorded,
            LearnerEventTypes.SourceViewed);

        var decision = S083Policy.Evaluate(events);

        Assert.Equal(S083LearningState.CriterionAvailable, decision.State);
        Assert.True(decision.CriterionRevealAllowed);
        Assert.Equal(
            PolicyDenialReason.None,
            S083Policy.AuthorizeEvent(events, LearnerEventTypes.ModelAnswerRevealed));
        Assert.Equal(["SOURCE"], S083Policy.ProjectSupportUsed(events));
        events.Add(Event(
            LearnerEventTypes.ModelAnswerRevealed,
            events.Count,
            JsonSerializer.SerializeToElement(new { acknowledged = true })));
        Assert.Equal(
            ["SOURCE", "CRITERION"],
            S083Policy.ProjectSupportUsed(events));
    }

    [Fact]
    public void ChangedContextRetrievalBecomesDueUnderControlledClock()
    {
        var dueAt = new DateTimeOffset(2026, 8, 26, 10, 0, 0, TimeSpan.Zero);
        var events = Timeline(
            LearnerEventTypes.LearningSessionStarted,
            LearnerEventTypes.ExpectationRecorded,
            LearnerEventTypes.PrerequisiteRecallAttempted,
            LearnerEventTypes.PrimingResponseSubmitted,
            LearnerEventTypes.UnpaidRemedyRecorded,
            LearnerEventTypes.SourceViewed,
            LearnerEventTypes.ModelAnswerRevealed,
            LearnerEventTypes.ComparisonRecorded,
            LearnerEventTypes.ProposalRevisionRecorded,
            LearnerEventTypes.ArtifactSubmitted);
        events.Add(Event(
            LearnerEventTypes.RetrievalScheduled,
            events.Count,
            JsonSerializer.SerializeToElement(new { dueAt })));

        var before = S083Policy.Evaluate(events, dueAt.AddSeconds(-1));
        var after = S083Policy.Evaluate(events, dueAt);

        Assert.Equal(S083LearningState.RetrievalScheduled, before.State);
        Assert.Empty(before.PermittedActions);
        Assert.Equal(S083LearningState.RetrievalDue, after.State);
        Assert.Contains(S083Action.CompleteRetrieval, after.PermittedActions);
        Assert.False(after.CriterionRevealAllowed);
        Assert.False(after.PaidProposalImprovementAllowed);
        Assert.Equal("NONE", after.MasteryEffect);
    }

    private static List<LearnerEventEnvelope> Timeline(params string[] eventTypes) =>
        eventTypes
            .Select((eventType, index) =>
                Event(eventType, index, JsonSerializer.SerializeToElement(new { })))
            .ToList();

    private static LearnerEventEnvelope Event(
        string eventType,
        int index,
        JsonElement payload) =>
        new(
            Guid.Parse($"00000000-0000-0000-0000-{index + 1:000000000000}"),
            eventType,
            1,
            DateTimeOffset.UnixEpoch.AddSeconds(index),
            DateTimeOffset.UnixEpoch.AddSeconds(index),
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            "S083",
            Revision,
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            null,
            $"test-key-{index:0000}",
            new EventActor("learner", "subject"),
            payload);
}
