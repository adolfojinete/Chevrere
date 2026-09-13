namespace Chevrere.SharedKernel.Audit;

public interface IAuditRecorder
{
    void Record(
        string action,
        string entityType,
        Guid entityId,
        Guid? tenantId,
        object? previousValue = null,
        object? newValue = null);
}
