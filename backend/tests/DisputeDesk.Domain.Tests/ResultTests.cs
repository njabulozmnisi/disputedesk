namespace DisputeDesk.Domain.Tests;

public class ResultTests
{
    private static readonly Error SomeError = Error.BusinessRule("not-disputable", "The transaction is older than 90 days.");

    [Fact]
    public void Success_NoValue_HasNoError()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Null(result.Error);
    }

    [Fact]
    public void ImplicitConversion_FromError_ReturnsFailedResult()
    {
        Result result = SomeError;

        Assert.True(result.IsFailure);
        Assert.False(result.IsSuccess);
        Assert.Same(SomeError, result.Error);
    }

    [Fact]
    public void Success_WithValue_ExposesValue()
    {
        var result = Result.Success(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void ImplicitConversion_FromValue_ReturnsSuccessfulResult()
    {
        Result<string> result = "ok";

        Assert.True(result.IsSuccess);
        Assert.Equal("ok", result.Value);
    }

    [Fact]
    public void ImplicitConversion_FromErrorToValueResult_ExposesError()
    {
        Result<string> result = SomeError;

        Assert.True(result.IsFailure);
        Assert.Same(SomeError, result.Error);
    }

    [Fact]
    public void Value_OnFailedResult_ThrowsInvalidOperationException()
    {
        Result<int> result = SomeError;

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => result.Value);
        Assert.Contains(SomeError.Code, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FailureOfT_AssignedToResult_IsFailure()
    {
        Result<int> failed = SomeError;
        Result result = failed;

        Assert.True(result.IsFailure);
    }
}
