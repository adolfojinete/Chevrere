namespace Chevrere.SharedKernel.Context;

public interface ICorrelationContext
{
    string CorrelationId { get; }

    void Set(string correlationId);
}
