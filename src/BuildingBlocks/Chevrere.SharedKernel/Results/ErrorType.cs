namespace Chevrere.SharedKernel.Results;

public enum ErrorType
{
    Validation = 1,
    NotFound = 2,
    Conflict = 3,
    Domain = 4,
    Unauthorized = 5,
    Forbidden = 6,
    Concurrency = 7,
    Failure = 8
}
