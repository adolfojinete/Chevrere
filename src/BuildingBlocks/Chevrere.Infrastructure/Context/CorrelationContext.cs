using Chevrere.SharedKernel.Context;

namespace Chevrere.Infrastructure.Context;

public sealed class CorrelationContext : ICorrelationContext
{
    private string _correlationId = string.Empty;

    public string CorrelationId => string.IsNullOrWhiteSpace(_correlationId)
        ? throw new InvalidOperationException("Correlation id has not been assigned.")
        : _correlationId;

    public void Set(string correlationId)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            throw new ArgumentException("Correlation id is required.", nameof(correlationId));
        }

        _correlationId = correlationId;
    }
}
