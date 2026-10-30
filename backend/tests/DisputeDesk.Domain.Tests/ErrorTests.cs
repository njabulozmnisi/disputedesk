namespace DisputeDesk.Domain.Tests;

public class ErrorTests
{
    private enum SampleStatus
    {
        UnderReview,
    }

    public static TheoryData<Error, ErrorKind> OneErrorPerKind => new()
    {
        { Error.NotFound("some-slug", "A message."), ErrorKind.NotFound },
        { Error.Validation("some-slug", "A message.", "Note"), ErrorKind.Validation },
        { Error.Forbidden("some-slug", "A message."), ErrorKind.Forbidden },
        { Error.ActionNotAllowed("some-slug", "A message.", "status", SampleStatus.UnderReview), ErrorKind.ActionNotAllowed },
        { Error.Conflict("some-slug", "A message."), ErrorKind.Conflict },
        { Error.BusinessRule("some-slug", "A message."), ErrorKind.BusinessRule },
        { Error.Unavailable("some-slug", "A message."), ErrorKind.Unavailable },
        { Error.RateLimited("some-slug", "A message."), ErrorKind.RateLimited },
    };

    [Fact]
    public void ErrorKind_Members_AreExactlyTheAd3Set()
    {
        string[] expected = ["ActionNotAllowed", "BusinessRule", "Conflict", "Forbidden", "NotFound", "RateLimited", "Unavailable", "Validation"];

        Assert.Equal(expected, Enum.GetNames<ErrorKind>().Order(StringComparer.Ordinal));
    }

    [Fact]
    public void OneErrorPerKind_AllKinds_HaveOneFactoryEach()
    {
        IEnumerable<ErrorKind> covered = OneErrorPerKind.Select(row => row.Data.Item2);

        Assert.Equal(Enum.GetValues<ErrorKind>().Order(), covered.Order());
    }

    [Theory]
    [MemberData(nameof(OneErrorPerKind))]
    public void Factory_AnyKind_SetsKindCodeAndMessage(Error error, ErrorKind kind)
    {
        Assert.Equal(kind, error.Kind);
        Assert.Equal("some-slug", error.Code);
        Assert.Equal("A message.", error.Message);
    }

    [Fact]
    public void Validation_WithField_PutsTheMessageUnderThatField()
    {
        var error = Error.Validation("validation", "The note is longer than 1000 characters.", "Note");

        Assert.Equal(["The note is longer than 1000 characters."], error.FieldErrors["Note"]);
        Assert.Empty(error.Extensions);
    }

    [Fact]
    public void ActionNotAllowed_WithCurrentStatus_AddsItAsTheNamedExtension()
    {
        var error = Error.ActionNotAllowed("action-not-allowed", "A resolved dispute cannot be withdrawn.", "customerStatus", SampleStatus.UnderReview);

        Assert.Equal(SampleStatus.UnderReview, error.Extensions["customerStatus"]);
        Assert.Empty(error.FieldErrors);
    }

    [Fact]
    public void NotFound_Created_HasNoFieldErrorsOrExtensions()
    {
        var error = Error.NotFound("not-found", "Not found.");

        Assert.Empty(error.FieldErrors);
        Assert.Empty(error.Extensions);
    }

    [Fact]
    public void Equals_SameKindCodeAndMessageWithoutFieldErrorsOrExtensions_ReturnsTrue()
    {
        Assert.Equal(Error.NotFound("not-found", "Not found."), Error.NotFound("not-found", "Not found."));
    }
}
