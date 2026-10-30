using DisputeDesk.Contracts.Disputes;

namespace DisputeDesk.Api.IntegrationTests.Disputes;

/// <summary>
/// A record's generated <c>ToString</c> prints every property, and a request's note is free text the customer may fill
/// with personal data, so a request body prints only its type name (OWASP Logging Cheat Sheet: data to exclude).
/// </summary>
public class RequestBodyPrivacyTests
{
    [Fact]
    public void RaiseDisputeRequest_ToString_PrintsOnlyTheTypeName()
    {
        var request = new RaiseDisputeRequest("account-marker", "transaction-marker", Reason.WrongAmount, "note-marker", "80.00");

        Assert.Equal(nameof(RaiseDisputeRequest), request.ToString());
    }

    [Fact]
    public void WithdrawDisputeRequest_ToString_PrintsOnlyTheTypeName()
    {
        var request = new WithdrawDisputeRequest("note-marker");

        Assert.Equal(nameof(WithdrawDisputeRequest), request.ToString());
    }
}
