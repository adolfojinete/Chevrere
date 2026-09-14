using Chevrere.SharedKernel.Results;

namespace Chevrere.SharedKernel.Persistence;

/// <summary>
/// Raised when optimistic concurrency (e.g. xmin) detects a conflicting update.
/// Infrastructure only translates the persistence error; callers decide recovery.
/// </summary>
public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException()
        : base("The resource was modified by another request.")
    {
    }

    public static Error ToError() =>
        Error.Concurrency(ErrorCodes.Concurrency, "The resource was modified by another request.");
}
