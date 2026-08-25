namespace FdeTutor.Persistence.Entities;

public sealed class PlatformUserEntity
{
    public Guid TenantId { get; set; }

    public Guid ObjectId { get; set; }

    public required string ExternalSubject { get; set; }

    public required string AuthenticationMode { get; set; }

    public required string RolesJson { get; set; }

    public DateTimeOffset FirstObservedAt { get; set; }

    public DateTimeOffset LastObservedAt { get; set; }
}
