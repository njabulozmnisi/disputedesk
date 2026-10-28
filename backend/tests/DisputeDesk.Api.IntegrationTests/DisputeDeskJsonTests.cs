using System.Text.Json;
using DisputeDesk.Contracts.Disputes;
using DisputeDesk.Contracts.Json;

namespace DisputeDesk.Api.IntegrationTests;

/// <summary>
/// The shared JSON settings (AD-5): an enum on the wire is exactly one member name in camelCase, so a list of names, an
/// unknown name or a number fails to bind instead of becoming another member or an undefined value.
/// </summary>
public class DisputeDeskJsonTests
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    [Theory]
    [InlineData("\"wrongAmount\"", Reason.WrongAmount)]
    [InlineData("\"refundNotProcessed\"", Reason.RefundNotProcessed)]
    public void Deserialize_OneMemberName_BindsThatMember(string json, Reason expected)
    {
        Assert.Equal(expected, JsonSerializer.Deserialize<Reason>(json, Options));
    }

    [Theory]
    [InlineData("\"duplicateCharge, wrongAmount\"")]       // the built-in converter alone reads NotReceived
    [InlineData("\"wrongAmount, cancelledSubscription\"")] // the built-in converter alone reads the undefined 6
    [InlineData("\"wrongAmount,wrongAmount\"")]
    [InlineData("\"wrongAmount\\u002C unrecognised\"")]     // an escaped comma
    public void Deserialize_CommaSeparatedNames_Throws(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Reason>(json, Options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Reason?>(json, Options));
    }

    [Theory]
    [InlineData("\"fraud\"")]
    [InlineData("\"\"")]
    [InlineData("\"6\"")]
    [InlineData("6")]
    public void Deserialize_UndefinedNameOrNumber_Throws(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Reason>(json, Options));
    }

    [Fact]
    public void Serialize_Member_WritesItsCamelCaseName()
    {
        Assert.Equal("\"underReviewPrioritised\"", JsonSerializer.Serialize(CustomerStatus.UnderReviewPrioritised, Options));
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        DisputeDeskJson.Apply(options);
        return options;
    }
}
