using DisputeDesk.Api.Disputes.Idempotency;
using Microsoft.Extensions.Primitives;

namespace DisputeDesk.Api.IntegrationTests.Disputes.Idempotency;

/// <summary>
/// AC1, the header's form as a fast unit test with no Api: an RFC 9651 String (§4.2.5) of 1 to 255 characters once
/// decoded, and nothing else.
/// </summary>
public class IdempotencyKeyHeaderTests
{
    public static TheoryData<string, string> Accepted => new()
    {
        { "\"8e03978e-40d5-43e8-bc93-6894a57f9324\"", "8e03978e-40d5-43e8-bc93-6894a57f9324" },
        { "\"a\"", "a" },
        { "   \"a\"  ", "a" },                         // spaces around the item are discarded
        { "\"a b\"", "a b" },                           // SP is allowed inside
        { "\"a\\\"b\"", "a\"b" },                       // escaped quote
        { "\"a\\\\b\"", "a\\b" },                       // escaped backslash
        { "\"!#$%&'()*+,-./:;<=>?@[]^_`{|}~\"", "!#$%&'()*+,-./:;<=>?@[]^_`{|}~" },
        { $"\"{new string('k', 255)}\"", new string('k', 255) },
        { $"\"{new string('k', 254)}\\\\\"", new string('k', 254) + "\\" }, // 255 once decoded, 256 escaped
    };

    [Theory]
    [MemberData(nameof(Accepted))]
    public void Parse_OneStringOf1To255Characters_ReturnsTheDecodedKey(string header, string key)
    {
        Assert.Equal(key, IdempotencyKeyHeader.Parse(new StringValues(header)));
    }

    public static TheoryData<string> Refused => new()
    {
        "",
        "   ",
        "abc",                                  // a Token, not a String
        "8e03978e-40d5-43e8-bc93-6894a57f9324", // unquoted
        "1",                                    // an Integer
        ":YWJj:",                               // a Byte Sequence
        "?1",                                   // a Boolean
        "\"\"",                                 // empty once decoded
        $"\"{new string('k', 256)}\"",          // 256 once decoded
        "\"abc",                                // no closing quote
        "abc\"",
        "'abc'",
        "\"a\\b\"",                             // a backslash escapes only a quote or a backslash
        "\"a\\\"",                              // the escape eats the closing quote
        "\"a\tb\"",                             // a control character
        "\"a\nb\"",
        "\"a\u007fb\"",                         // DEL
        "\"é\"",                                // non-ASCII
        "\"a\" b",                              // something after the item
        "\"a\";p=1",                            // a parameter: the draft defines none
        "\"a\",\"b\"",                          // a list
        "\"a\"\"b\"",
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public void Parse_AnythingButOneStringOf1To255Characters_ReturnsNull(string header)
    {
        Assert.Null(IdempotencyKeyHeader.Parse(new StringValues(header)));
    }

    [Fact]
    public void Parse_NoHeader_ReturnsNull()
    {
        Assert.Null(IdempotencyKeyHeader.Parse(StringValues.Empty));
    }

    [Fact]
    public void Parse_TwoHeaderLines_ReturnsNullAsTheyCombineIntoAList()
    {
        Assert.Null(IdempotencyKeyHeader.Parse(new StringValues(["\"a\"", "\"a\""])));
    }
}
