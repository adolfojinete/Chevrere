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

    /// <summary>
    /// Detaches pending (Added) audit rows for a failed write attempt so they are not persisted later.
    /// </summary>
    void DiscardPending(string action, string entityType, Guid entityId);
}
