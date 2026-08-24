using FdeTutor.Contracts.Api;
using FdeTutor.Contracts.Events;
using FdeTutor.Contracts.Policy;

namespace FdeTutor.Api.Learning;

public sealed record S083CommandResult(
    AppendEventResult? AppendResult,
    S083PolicyDecision Policy,
    ApiError? Error)
{
    public bool IsAccepted => AppendResult is not null && Error is null;
}
