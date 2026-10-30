using System.Net;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Application.Audit;
using DisputeDesk.Contracts.Audit;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests.Disputes.Withdraw;

/// <summary>
/// AC8 for withdraw (AD-14a, FR-10): each refusal on rule or status (404, 409) writes one <c>Rejected</c> audit row
/// through <c>AuditLog.Record</c>, every column checked; validation errors, authorisation refusals and a success write
/// none; and a failed audit save answers 500. The clash outcomes are in <see cref="WithdrawConcurrencyTests"/>.
/// </summary>
public class WithdrawRefusalAuditTests(PostgresFixture postgres, TransactionsStub stub) : IClassFixture<TransactionsStub>
{
    [Fact]
    public async Task Withdraw_AnotherCustomersDisputeAndAnUnknownId_EachAuditOneRejectedRowWithNoDisputeAndNothingOfB()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient customerB = await factory.SignInLinkedAsync("login-b", TransactionsApi.CustomerB);
        Guid theirs = (await customerB.RaiseCreatedAsync(DisputesApi.Body("TXN-B-0027", "ACC-B-1"))).GetProperty("disputeId").GetGuid();
        using HttpClient customerA = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);

        Assert.Equal(HttpStatusCode.NotFound, (await customerA.WithdrawAsync(theirs.ToString())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await customerA.WithdrawAsync(Guid.CreateVersion7().ToString())).StatusCode);

        List<AuditLogEntry> rows = await factory.DisputeAuditRowsAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => row.AssertRejectedOfA(AuditAction.Withdraw, stub.NowAsStored, disputeId: null));
        Assert.All(rows, row => Assert.DoesNotContain(TransactionsApi.CustomerB, Columns(row)));
        Assert.All(rows, row => Assert.DoesNotContain(theirs.ToString(), Columns(row)));
        Assert.Equal(WithoutPerRequestColumns(rows[0]), WithoutPerRequestColumns(rows[1]));
    }

    [Theory]
    [InlineData(DisputeStatus.Resolved)]
    [InlineData(DisputeStatus.Withdrawn)]
    public async Task Withdraw_ClosedDispute_AuditsOneRejectedRowForTheDispute(DisputeStatus status)
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        Guid disputeId = (await browser.RaiseCreatedAsync(DisputesApi.Body())).GetProperty("disputeId").GetGuid();
        StoredEvent closing = status == DisputeStatus.Resolved
            ? new DisputeResolved(DisputeStatus.Submitted, DisputeStatus.Resolved, Outcome.Upheld, "Refunded")
            : new DisputeWithdrawn(DisputeStatus.Submitted, DisputeStatus.Withdrawn, null);
        await factory.AppendAsync(
            disputeId, status, status == DisputeStatus.Resolved ? Outcome.Upheld : null,
            closing with { Version = 2, OccurredAtUtc = stub.NowAsStored, ActorType = ActorType.Operations, ActorId = "ops-a" });

        Assert.Equal(HttpStatusCode.Conflict, (await browser.WithdrawAsync(disputeId.ToString())).StatusCode);

        Assert.Single(await factory.DisputeAuditRowsAsync()).AssertRejectedOfA(AuditAction.Withdraw, stub.NowAsStored, disputeId);
    }

    [Fact]
    public async Task Withdraw_SuccessValidationAndAuthorisationRefusals_AuditNothing()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        string disputeId = (await browser.RaiseCreatedAsync(DisputesApi.Body())).GetProperty("disputeId").GetString() ?? "";
        using HttpClient anonymous = factory.CreateBrowser(new CookieContainer());
        using HttpClient unlinked = factory.CreateBrowser(new CookieContainer());
        await unlinked.SignInAsync(factory.Zitadel, "unlinked-login");

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.WithdrawAsync(disputeId, "{}", antiforgeryToken: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await unlinked.WithdrawAsync(disputeId)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.WithdrawAsync(disputeId, withAntiforgeryToken: false)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.WithdrawAsync(disputeId, "[]")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.WithdrawAsync("not-a-guid")).StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType,
            (await browser.WithdrawAsync(disputeId, "{}", await browser.AntiforgeryTokenAsync(), mediaType: "text/plain")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.WithdrawAsync(disputeId, DisputesApi.WithdrawBody(new string('a', 501)))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await browser.WithdrawAsync(disputeId)).StatusCode);

        Assert.Empty(await factory.DisputeAuditRowsAsync());
    }

    [Fact]
    public async Task Withdraw_AuditSaveFails_Returns500()
    {
        var failAudit = new FailAuditInsertInterceptor { Armed = false };
        await using ApiFactory factory = await TransactionsApi.StartAsync(
            postgres, stub, services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(failAudit)));
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        // Armed only after sign-in, which audits too, so only the refusal's entry fails.
        failAudit.Armed = true;

        HttpResponseMessage response = await browser.WithdrawAsync(Guid.CreateVersion7().ToString());

        Assert.Equal(1, failAudit.Failed);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Empty(await factory.DisputeAuditRowsAsync());
    }

    private static string?[] Columns(AuditLogEntry row) =>
        [row.Id.ToString(), row.Action.ToString(), row.Result.ToString(), row.ActorType.ToString(), row.ActorId, row.CustomerId, row.DisputeId?.ToString(), row.TraceId];

    /// <summary>Every column but <c>Id</c>, <c>OccurredAtUtc</c> and <c>TraceId</c>.</summary>
    private static (AuditAction, AuditResult, ActorType, string, string?, Guid?) WithoutPerRequestColumns(AuditLogEntry row) =>
        (row.Action, row.Result, row.ActorType, row.ActorId, row.CustomerId, row.DisputeId);
}
