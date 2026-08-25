using FdeTutor.Domain.Authorization;

namespace FdeTutor.Domain.Tests;

public sealed class AuthorizationMatrixTests
{
    [Fact]
    public void MatrixDefinesEveryG02RoleExactlyOnce()
    {
        Assert.Equal(PlatformRoles.All, Phase1AuthorizationMatrix.Roles.Select(role => role.Id));
        Assert.Equal(PlatformRoles.All.Count, Phase1AuthorizationMatrix.Roles.Count);
    }

    [Fact]
    public void EffectiveCapabilitiesRequireAnExplicitAllow()
    {
        var learner = Phase1AuthorizationMatrix.EffectiveCapabilities(
            new HashSet<string>([PlatformRoles.Learner], StringComparer.Ordinal));
        var reviewer = Phase1AuthorizationMatrix.EffectiveCapabilities(
            new HashSet<string>([PlatformRoles.Reviewer], StringComparer.Ordinal));

        Assert.Contains("s083.read-own", learner);
        Assert.Contains("s083.append-own", learner);
        Assert.DoesNotContain("s083.read-own", reviewer);
        Assert.DoesNotContain("evidence.review-assigned", reviewer);
    }

    [Fact]
    public void EventsAreNeverMutableAndRoleAssignmentStaysExternal()
    {
        var eventMutation = Phase1AuthorizationMatrix.Capabilities.Single(
            capability => capability.Id == "events.mutate");
        var roleAssignment = Phase1AuthorizationMatrix.Capabilities.Single(
            capability => capability.Id == "users.assign-roles");

        Assert.All(
            eventMutation.Access.Values,
            disposition => Assert.Equal(AuthorizationDisposition.Deny, disposition));
        Assert.Equal(
            AuthorizationDisposition.External,
            roleAssignment.Access[PlatformRoles.Administrator]);
    }
}
