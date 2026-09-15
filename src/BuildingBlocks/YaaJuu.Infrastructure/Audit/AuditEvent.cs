namespace YaaJuu.Infrastructure.Audit;

public sealed class AuditEvent
{
    public Guid Id { get; set; }

    public Guid? TenantId { get; set; }

    public Guid? ActorUserId { get; set; }

    public required string Action { get; set; }

    public required string EntityType { get; set; }

    public Guid EntityId { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public required string CorrelationId { get; set; }

    public string? PreviousValue { get; set; }

    public string? NewValue { get; set; }
}
