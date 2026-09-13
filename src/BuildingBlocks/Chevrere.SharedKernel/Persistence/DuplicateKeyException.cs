namespace Chevrere.SharedKernel.Persistence;

/// <summary>
/// Raised when a unique constraint prevents inserting a duplicate row.
/// Callers of idempotent creates may treat this as a recoverable race.
/// </summary>
public sealed class DuplicateKeyException : Exception
{
    public DuplicateKeyException()
        : base("A unique constraint was violated.")
    {
    }

    public DuplicateKeyException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
