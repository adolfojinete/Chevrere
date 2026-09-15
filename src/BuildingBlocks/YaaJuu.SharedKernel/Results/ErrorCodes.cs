namespace YaaJuu.SharedKernel.Results;

public static class ErrorCodes
{
    public const string Validation = "validation.failed";
    public const string NotFound = "not_found";
    public const string Conflict = "conflict";
    public const string Unauthorized = "unauthorized";
    public const string Forbidden = "forbidden";
    public const string Concurrency = "concurrency.conflict";
    public const string InvalidState = "domain.invalid_state";
}
