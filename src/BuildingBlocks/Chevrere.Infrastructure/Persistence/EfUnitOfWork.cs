using Chevrere.SharedKernel.Persistence;
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
            // Translate only. Do not mutate ChangeTracker: recovery belongs to the use case.
            throw new DuplicateKeyException("A unique constraint was violated.", ex);
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
