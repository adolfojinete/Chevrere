using System.Text.Json;
using Chevrere.Infrastructure.Persistence;
using Chevrere.SharedKernel.Audit;
using Chevrere.SharedKernel.Context;
using Chevrere.SharedKernel.Time;

namespace Chevrere.Infrastructure.Audit;

public sealed class AuditRecorder(
    ChevrereDbContext dbContext,
    ICurrentUser currentUser,
    ICorrelationContext correlation,
    IClock clock) : IAuditRecorder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Record(
        string action,
        string entityType,
        Guid entityId,
        Guid? tenantId,
        object? previousValue = null,
        object? newValue = null)
    {
        dbContext.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            ActorUserId = currentUser.UserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            OccurredAt = clock.UtcNow,
            CorrelationId = SafeCorrelationId(),
            PreviousValue = Serialize(previousValue),
            NewValue = Serialize(newValue)
        });
    }

    private string SafeCorrelationId()
    {
        try
        {
            return correlation.CorrelationId;
        }
        catch (InvalidOperationException)
        {
            return Guid.CreateVersion7().ToString("D");
        }
    }

    private static string? Serialize(object? value) =>
        value is null ? null : JsonSerializer.Serialize(value, JsonOptions);
}
