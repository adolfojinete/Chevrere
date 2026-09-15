namespace YaaJuu.SharedKernel.Persistence;

/// <summary>
/// Raised when a unique constraint prevents inserting a duplicate row.
/// Infrastructure only translates the persistence error; callers decide recovery.
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
