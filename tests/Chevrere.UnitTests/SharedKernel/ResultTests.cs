using Chevrere.SharedKernel.Results;

namespace Chevrere.UnitTests.SharedKernel;

public sealed class ResultTests
{
    [Fact]
    public void Success_has_no_error()
    {
        var result = Result.Success();
        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Failure_exposes_error()
    {
        var error = Error.NotFound("x", "missing");
        var result = Result.Failure(error);
        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public void Generic_success_returns_value()
    {
        var result = Result.Success(42);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Generic_failure_cannot_read_value()
    {
        var result = Result.Failure<int>(Error.Domain("e", "no"));
        Assert.Throws<InvalidOperationException>(() => _ = result.Value);
    }

    [Fact]
    public void Error_factories_set_type()
    {
        Assert.Equal(ErrorType.Validation, Error.Validation("a", "b").Type);
        Assert.Equal(ErrorType.Conflict, Error.Conflict("a", "b").Type);
        Assert.Equal(ErrorType.Unauthorized, Error.Unauthorized("a", "b").Type);
        Assert.Equal(ErrorType.Forbidden, Error.Forbidden("a", "b").Type);
        Assert.Equal(ErrorType.Concurrency, Error.Concurrency("a", "b").Type);
    }

    [Fact]
    public void Paged_result_computes_pages()
    {
        var page = new PagedResult<int>([1, 2], 1, 20, 42);
        Assert.Equal(3, page.TotalPages);
    }
}
