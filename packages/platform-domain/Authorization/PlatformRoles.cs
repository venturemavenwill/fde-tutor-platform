namespace FdeTutor.Domain.Authorization;

public static class PlatformRoles
{
    public const string Learner = "Learner";
    public const string Instructor = "Instructor";
    public const string Reviewer = "Reviewer";
    public const string Author = "Author";
    public const string Administrator = "Administrator";
    public const string Operator = "Operator";

    public static IReadOnlyList<string> All { get; } =
    [
        Learner,
        Instructor,
        Reviewer,
        Author,
        Administrator,
        Operator,
    ];

    public static bool IsKnown(string role) =>
        All.Contains(role, StringComparer.Ordinal);
}
