using DisputeDesk.Application.Disputes;
using DisputeDesk.Contracts.Disputes;
using DisputeDesk.Domain;
using DisputeDesk.Domain.Disputes;

namespace DisputeDesk.Api.IntegrationTests.Disputes;

/// <summary>The errors that carry a wire value, built in Application from the Domain base errors (AD-1, AD-13).</summary>
public class DisputeWireErrorTests
{
    [Fact]
    public void ActionNotAllowedAs_CustomerStatus_CarriesItUnderCustomerStatus()
    {
        Error error = DisputeWire.ActionNotAllowedAs(CustomerStatus.Withdrawn);

        Assert.Equal(ErrorKind.ActionNotAllowed, error.Kind);
        Assert.Equal("action-not-allowed", error.Code);
        Assert.Equal(Domain.Disputes.DisputeErrors.ActionNotAllowed.Message, error.Message);
        Assert.Equal(["customerStatus"], error.Extensions.Keys);
        Assert.Equal(CustomerStatus.Withdrawn, error.Extensions["customerStatus"]);
    }

    [Fact]
    public void ActionNotAllowedWithDisputeStatus_CarriesTheStatusUnderDisputeStatus()
    {
        Error error = DisputeWire.ActionNotAllowedWithDisputeStatus(DisputeStatus.Escalated);

        Assert.Equal(ErrorKind.ActionNotAllowed, error.Kind);
        Assert.Equal("action-not-allowed", error.Code);
        Assert.Equal(Domain.Disputes.DisputeErrors.ActionNotAllowed.Message, error.Message);
        Assert.Equal(["disputeStatus"], error.Extensions.Keys);
        Assert.Equal(DisputeStatus.Escalated, error.Extensions["disputeStatus"]);
    }

    [Fact]
    public void AlreadyDisputedAs_DisputeAndCustomerStatus_CarriesBoth()
    {
        var disputeId = Guid.NewGuid();

        Error error = DisputeWire.AlreadyDisputedAs(disputeId, CustomerStatus.UnderReviewPrioritised);

        Assert.Equal(ErrorKind.Conflict, error.Kind);
        Assert.Equal("already-disputed", error.Code);
        Assert.Equal(Domain.Disputes.DisputeErrors.AlreadyDisputed.Message, error.Message);
        Assert.Equal(disputeId, error.Extensions["disputeId"]);
        Assert.Equal(CustomerStatus.UnderReviewPrioritised, error.Extensions["customerStatus"]);
    }
}
