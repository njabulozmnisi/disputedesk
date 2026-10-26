namespace DisputeDesk.Domain.Tests;

public class ErrorTests
{
    public static TheoryData<Func<string, string, Error>, ErrorKind> Factories => new()
    {
        { Error.NotFound, ErrorKind.NotFound },
        { Error.Validation, ErrorKind.Validation },
        { Error.Forbidden, ErrorKind.Forbidden },
        { Error.ActionNotAllowed, ErrorKind.ActionNotAllowed },
        { Error.Conflict, ErrorKind.Conflict },
        { Error.BusinessRule, ErrorKind.BusinessRule },
        { Error.Unavailable, ErrorKind.Unavailable },
    };

    [Fact]
    public void ErrorKind_Members_AreExactlyTheAd3Set()
    {
        string[] expected = ["ActionNotAllowed", "BusinessRule", "Conflict", "Forbidden", "NotFound", "Unavailable", "Validation"];

        Assert.Equal(expected, Enum.GetNames<ErrorKind>().Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Factories_AllKinds_HaveOneFactoryEach()
    {
        IEnumerable<ErrorKind> covered = Factories.Select(row => row.Data.Item2);

        Assert.Equal(Enum.GetValues<ErrorKind>().Order(), covered.Order());
    }

    [Theory]
    [MemberData(nameof(Factories))]
    public void Factory_AnyKind_SetsKindCodeAndMessage(Func<string, string, Error> factory, ErrorKind kind)
    {
        Error error = factory("some-slug", "A message.");

        Assert.Equal(kind, error.Kind);
        Assert.Equal("some-slug", error.Code);
        Assert.Equal("A message.", error.Message);
    }

    [Fact]
    public void Equals_SameKindCodeAndMessage_ReturnsTrue()
    {
        Assert.Equal(Error.NotFound("not-found", "Not found."), Error.NotFound("not-found", "Not found."));
    }
}
