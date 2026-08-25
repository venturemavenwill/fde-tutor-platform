namespace FdeTutor.Domain.Authorization;

public enum AuthorizationDisposition
{
    Allow,
    Deny,
    Deferred,
    External,
}

public sealed record PlatformRoleDefinition(
    string Id,
    string Label,
    string Description);

public sealed record PlatformCapabilityDefinition(
    string Id,
    string Label,
    string Constraint,
    IReadOnlyDictionary<string, AuthorizationDisposition> Access);

public static class Phase1AuthorizationMatrix
{
    public const string Version = "g02-phase1-1";

    public static IReadOnlyList<PlatformRoleDefinition> Roles { get; } =
    [
        new(
            PlatformRoles.Learner,
            "Learner",
            "Completes and revisits the learner's own S083 work."),
        new(
            PlatformRoles.Instructor,
            "Instructor",
            "Future cohort guidance role; no Phase 1 learner-evidence surface."),
        new(
            PlatformRoles.Reviewer,
            "Reviewer",
            "Future scoped review role; no Phase 1 review workflow."),
        new(
            PlatformRoles.Author,
            "Author",
            "Future content publishing role; no learner-evidence access."),
        new(
            PlatformRoles.Administrator,
            "Learning administrator",
            "Manages Entra assignment and tenant-scoped directory metadata without raw evidence."),
        new(
            PlatformRoles.Operator,
            "Platform operator",
            "Runs deployment and recovery operations without raw learner evidence by default."),
    ];

    public static IReadOnlyList<PlatformCapabilityDefinition> Capabilities { get; } =
    [
        Capability(
            "identity.read-own",
            "Read own identity and access",
            "Returns only the validated caller's tenant, object ID, roles, and effective capabilities.",
            (PlatformRoles.Learner, AuthorizationDisposition.Allow),
            (PlatformRoles.Instructor, AuthorizationDisposition.Allow),
            (PlatformRoles.Reviewer, AuthorizationDisposition.Allow),
            (PlatformRoles.Author, AuthorizationDisposition.Allow),
            (PlatformRoles.Administrator, AuthorizationDisposition.Allow),
            (PlatformRoles.Operator, AuthorizationDisposition.Allow)),
        Capability(
            "s083.read-own",
            "Read own S083 state",
            "Every object lookup is constrained by the caller's validated tenant and object ID.",
            (PlatformRoles.Learner, AuthorizationDisposition.Allow)),
        Capability(
            "s083.append-own",
            "Append own S083 learning command",
            "Commands remain subject to deterministic policy, revision, and idempotency checks.",
            (PlatformRoles.Learner, AuthorizationDisposition.Allow)),
        Capability(
            "directory.read-tenant",
            "Read tenant user directory metadata",
            "Returns durable subject, observed roles, and timestamps; never email or learner responses.",
            (PlatformRoles.Administrator, AuthorizationDisposition.Allow)),
        Capability(
            "users.assign-roles",
            "Assign users and app roles",
            "Assignments are made in the Microsoft Entra enterprise application, not this API.",
            (PlatformRoles.Administrator, AuthorizationDisposition.External)),
        Capability(
            "learners.read-assigned",
            "Read assigned learner evidence",
            "Cohort and object-scoped instructor/reviewer access is not implemented in Phase 1.",
            (PlatformRoles.Instructor, AuthorizationDisposition.Deferred),
            (PlatformRoles.Reviewer, AuthorizationDisposition.Deferred)),
        Capability(
            "evidence.review-assigned",
            "Review assigned evidence",
            "Review queues, decisions, recusal, and overrides remain Phase 3 behavior.",
            (PlatformRoles.Reviewer, AuthorizationDisposition.Deferred)),
        Capability(
            "content.publish",
            "Publish a content package",
            "Phase 1 consumes validated packages; interactive author publishing is deferred.",
            (PlatformRoles.Author, AuthorizationDisposition.Deferred),
            (PlatformRoles.Operator, AuthorizationDisposition.External)),
        Capability(
            "projection.rebuild",
            "Rebuild projections",
            "Available through controlled platform operations, not a browser endpoint.",
            (PlatformRoles.Operator, AuthorizationDisposition.External)),
        Capability(
            "events.mutate",
            "Update or delete learner events",
            "Learner events are append-only; corrections append new events.",
            []),
    ];

    public static IReadOnlyList<string> EffectiveCapabilities(
        IReadOnlySet<string> roles) =>
        Capabilities
            .Where(capability => roles.Any(role =>
                capability.Access.TryGetValue(role, out var disposition) &&
                disposition == AuthorizationDisposition.Allow))
            .Select(capability => capability.Id)
            .ToArray();

    private static PlatformCapabilityDefinition Capability(
        string id,
        string label,
        string constraint,
        params (string Role, AuthorizationDisposition Disposition)[] overrides)
    {
        var access = PlatformRoles.All.ToDictionary(
            role => role,
            _ => AuthorizationDisposition.Deny,
            StringComparer.Ordinal);
        foreach (var (role, disposition) in overrides)
        {
            access[role] = disposition;
        }

        return new PlatformCapabilityDefinition(id, label, constraint, access);
    }
}
