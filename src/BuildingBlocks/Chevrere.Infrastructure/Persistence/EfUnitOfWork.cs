using Chevrere.SharedKernel.Persistence;
using Chevrere.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

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
