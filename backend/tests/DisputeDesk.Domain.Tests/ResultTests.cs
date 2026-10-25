namespace DisputeDesk.Domain.Tests;

public class ResultTests
{
    private static readonly Error SomeError = Error.BusinessRule("dispute.window_closed", "The dispute window has closed.");

    [Fact]
    public void Success_has_no_error()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Failure_exposes_the_error()
    {
        var result = Result.Failure(SomeError);

        Assert.True(result.IsFailure);
        Assert.False(result.IsSuccess);
        Assert.Same(SomeError, result.Error);
    }

    [Fact]
    public void An_error_converts_implicitly_to_a_failed_result()
    {
        Result result = SomeError;

        Assert.True(result.IsFailure);
        Assert.Same(SomeError, result.Error);
    }

    [Fact]
    public void Failure_rejects_a_null_error()
    {
        Assert.Throws<ArgumentNullException>(() => Result.Failure(null!));
        Assert.Throws<ArgumentNullException>(() => Result.Failure<string>(null!));
    }

    [Fact]
    public void Successful_value_result_exposes_the_value()
    {
        var result = Result.Success(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void A_value_converts_implicitly_to_a_successful_result()
    {
        Result<string> result = "ok";

        Assert.True(result.IsSuccess);
        Assert.Equal("ok", result.Value);
    }

    [Fact]
    public void Failed_value_result_exposes_the_error()
    {
        Result<string> result = SomeError;

        Assert.True(result.IsFailure);
        Assert.Same(SomeError, result.Error);
    }

    [Fact]
    public void Reading_the_value_of_a_failed_result_throws()
    {
        var result = Result.Failure<int>(SomeError);

        var exception = Assert.Throws<InvalidOperationException>(() => result.Value);
        Assert.Contains(SomeError.Code, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Success_rejects_a_null_value()
    {
        Assert.Throws<ArgumentNullException>(() => Result.Success<string>(null!));
    }

    [Fact]
    public void A_failed_value_result_is_also_a_plain_result()
    {
        Result result = Result.Failure<int>(SomeError);

        Assert.True(result.IsFailure);
    }
}

public class ErrorTests
{
    public static TheoryData<Func<string, string, Error>, ErrorKind> Factories => new()
    {
        { Error.NotFound, ErrorKind.NotFound },
        { Error.InvalidTransition, ErrorKind.InvalidTransition },
        { Error.Validation, ErrorKind.Validation },
        { Error.BusinessRule, ErrorKind.BusinessRule },
    };

    [Theory]
    [MemberData(nameof(Factories))]
    public void Each_factory_sets_its_kind_code_and_message(Func<string, string, Error> factory, ErrorKind kind)
    {
        var error = factory("dispute.code", "A message.");

        Assert.Equal(kind, error.Kind);
        Assert.Equal("dispute.code", error.Code);
        Assert.Equal("A message.", error.Message);
    }

    [Fact]
    public void Errors_with_the_same_parts_are_equal()
    {
        Assert.Equal(Error.NotFound("dispute.not_found", "Not found."), Error.NotFound("dispute.not_found", "Not found."));
    }
}
