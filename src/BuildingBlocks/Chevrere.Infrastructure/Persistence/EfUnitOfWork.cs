using Chevrere.SharedKernel.Persistence;
using Chevrere.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Chevrere.Infrastructure.Persistence;

public sealed class EfUnitOfWork(ChevrereDbContext dbContext) : IUnitOfWork
{
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            DetachPendingChanges();
            throw new DuplicateKeyException("A unique constraint was violated.", ex);
        }
    }

    private void DetachPendingChanges()
    {
        foreach (var entry in dbContext.ChangeTracker.Entries()
                     .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                     .ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                return true;
            }
        }

        return false;
    }
}

public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException()
        : base("The resource was modified by another request.")
    {
    }

    public static Error ToError() =>
        Error.Concurrency(ErrorCodes.Concurrency, "The resource was modified by another request.");
}
