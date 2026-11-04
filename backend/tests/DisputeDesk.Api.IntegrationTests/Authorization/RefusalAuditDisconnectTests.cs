using System.Text;
using DisputeDesk.Api.IntegrationTests.Disputes;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Operations;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Application.Audit;
using DisputeDesk.Contracts.Audit;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests.Authorization;

/// <summary>
/// 7.13 AC4 (NFR-SEC-10; OWASP Logging Cheat Sheet): each of the five refusals saves its <c>Denied</c> or <c>Rejected</c>
/// entry even when the client disconnects while that save is held, before its INSERT, or during a read that comes after
/// the refusal is decided and before its save. Each entry keeps its usual fields (AD-14a).
/// </summary>
public class RefusalAuditDisconnectTests(PostgresFixture postgres, TransactionsStub stub) : IClassFixture<TransactionsStub>
{
    // The withdraw's and the operations action's one read of the Disputes row after a second version clash (AD-15).
    private const string LastReadOfTheStatus = "SELECT d.\"Status\"";
    private const string DisputesRead = "FROM \"Disputes\"";

    [Fact]
    public async Task OperationsCallWithoutTheRole_ClientDisconnectsDuringTheAuditSave_CommitsTheDeniedEntry()
    {
        var hold = new HoldAuditSaveInterceptor(AuditAction.DisputeRead);
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub, hold.AddTo);
        using HttpClient tool = factory.CreateTool();
        var disputeId = Guid.CreateVersion7();

        Assert.True(await hold.SendAndDisconnectAsync(tool, OperationsRequest(HttpMethod.Get, $"{disputeId}", factory.Zitadel.Issue())));

        AssertOperationsEntry(Assert.Single(await factory.AuditRowsAsync()), AuditAction.DisputeRead, AuditResult.Denied, disputeId);
    }

    [Fact]
    public async Task RaiseOfAnAlreadyDisputedTransaction_ClientDisconnectsDuringTheAuditSave_CommitsTheRejectedEntry()
    {
        var hold = new HoldAuditSaveInterceptor(AuditAction.Raise);
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub, hold.AddTo);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        Guid disputeId = (await browser.RaiseCreatedAsync(DisputesApi.Body())).GetProperty("disputeId").GetGuid();
        using HttpRequestMessage raise = await CustomerRequestAsync(browser, DisputesApi.RaisePath, DisputesApi.Body());
        raise.Headers.TryAddWithoutValidation("Idempotency-Key", DisputesApi.NewKey());

        Assert.True(await hold.SendAndDisconnectAsync(browser, raise));

        Assert.Single(await factory.DisputeAuditRowsAsync()).AssertRejectedOfA(AuditAction.Raise, stub.NowAsStored, disputeId);
    }

    [Fact]
    public async Task RaiseThatLosesTheRaceToTheUniqueIndex_ClientDisconnectsDuringTheReadOfTheWinner_CommitsTheRejectedEntry()
    {
        // The raise's first read of Disputes is its "already disputed?" check. While that read is held, another raise of
        // the same transaction commits, so the save loses to the unique index; the second read, of the winner, is then
        // held until the client has gone.
        var checkRead = new HoldInterceptor(DisputesRead);
        var hold = new HoldAuditSaveInterceptor(AuditAction.Raise, readBeforeTheSave: DisputesRead, readNumber: 2);
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub, services =>
        {
            hold.AddTo(services);
            services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(checkRead));
        });
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        using HttpRequestMessage raise = await CustomerRequestAsync(browser, DisputesApi.RaisePath, DisputesApi.Body());
        raise.Headers.TryAddWithoutValidation("Idempotency-Key", DisputesApi.NewKey());

        Task<bool> committed = hold.SendAndDisconnectAsync(browser, raise);
        await checkRead.Reached.WaitAsync(TestContext.Current.CancellationToken);
        Guid winner = (await factory.DisputeInAsync(DisputeStatus.Submitted, raisedAt: stub.NowAsStored)).Id;
        checkRead.Release();

        Assert.True(await committed);
        Assert.Single(await factory.DisputeAuditRowsAsync()).AssertRejectedOfA(AuditAction.Raise, stub.NowAsStored, winner);
    }

    [Fact]
    public async Task WithdrawAfterTwoVersionClashes_ClientDisconnectsDuringTheReadOfTheStatus_CommitsTheRejectedEntry()
    {
        var appendFirst = new AppendNextVersionInterceptor();
        var hold = new HoldAuditSaveInterceptor(AuditAction.Withdraw, readBeforeTheSave: LastReadOfTheStatus);
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub, services =>
        {
            hold.AddTo(services);
            services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(appendFirst));
        });
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        Guid disputeId = (await browser.RaiseCreatedAsync(DisputesApi.Body())).GetProperty("disputeId").GetGuid();
        appendFirst.Arm(Next(new ReviewStarted(DisputeStatus.Submitted, DisputeStatus.UnderReview), disputeId, 2), DisputeStatus.UnderReview);
        appendFirst.Arm(Next(new DisputeEscalated(DisputeStatus.UnderReview, DisputeStatus.Escalated), disputeId, 3), DisputeStatus.Escalated);

        using HttpRequestMessage withdraw = await CustomerRequestAsync(browser, DisputesApi.WithdrawPath(disputeId.ToString()), "{}");
        Assert.True(await hold.SendAndDisconnectAsync(browser, withdraw));

        Assert.Equal(2, appendFirst.Appended);
        Assert.Single(await factory.DisputeAuditRowsAsync()).AssertRejectedOfA(AuditAction.Withdraw, stub.NowAsStored, disputeId);
    }

    [Fact]
    public async Task WithdrawOfAWithdrawnDispute_ClientDisconnectsDuringTheAuditSave_CommitsTheRejectedEntry()
    {
        var hold = new HoldAuditSaveInterceptor(AuditAction.Withdraw);
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub, hold.AddTo);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        Guid disputeId = (await browser.RaiseCreatedAsync(DisputesApi.Body())).GetProperty("disputeId").GetGuid();
        await browser.WithdrawOkAsync(disputeId.ToString());

        using HttpRequestMessage withdraw = await CustomerRequestAsync(browser, DisputesApi.WithdrawPath(disputeId.ToString()), "{}");
        Assert.True(await hold.SendAndDisconnectAsync(browser, withdraw));

        Assert.Single(await factory.DisputeAuditRowsAsync()).AssertRejectedOfA(AuditAction.Withdraw, stub.NowAsStored, disputeId);
    }

    [Fact]
    public async Task OperationsActionNotAllowedInTheStatus_ClientDisconnectsDuringTheAuditSave_CommitsTheRejectedEntry()
    {
        var hold = new HoldAuditSaveInterceptor(AuditAction.Resolve);
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub, hold.AddTo);
        Guid disputeId = await factory.RaiseForCustomerAAsync();
        using HttpClient tool = factory.CreateTool();
        // A submitted dispute cannot be resolved before its review starts.
        using HttpRequestMessage resolve = OperationsRequest(
            HttpMethod.Post, $"{disputeId}/resolve", factory.Zitadel.Issue(roles: OperationsApi.ResolveRole));
        resolve.Content = new StringContent(OperationsApi.ResolveBody(), Encoding.UTF8, "application/json");

        Assert.True(await hold.SendAndDisconnectAsync(tool, resolve));

        AuditLogEntry entry = Assert.Single(await factory.AuditRowsAsync(), row => row.ActorType == ActorType.Operations);
        AssertOperationsEntry(entry, AuditAction.Resolve, AuditResult.Rejected, disputeId);
    }

    [Fact]
    public async Task OperationsActionAfterTwoVersionClashes_ClientDisconnectsDuringTheReadOfTheStatus_CommitsTheRejectedEntry()
    {
        var appendFirst = new AppendNextVersionInterceptor();
        var hold = new HoldAuditSaveInterceptor(AuditAction.Resolve, readBeforeTheSave: LastReadOfTheStatus);
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub, services =>
        {
            hold.AddTo(services);
            services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(appendFirst));
        });
        Guid disputeId = (await factory.DisputeInAsync(DisputeStatus.UnderReview)).Id;
        // Each clash leaves the dispute escalated, which resolve allows, so the retry decides again and only the last
        // read of the Disputes row gives the 409.
        appendFirst.Arm(Next(new DisputeEscalated(DisputeStatus.UnderReview, DisputeStatus.Escalated), disputeId, 3), DisputeStatus.Escalated);
        appendFirst.Arm(Next(new DisputeEscalated(DisputeStatus.Escalated, DisputeStatus.Escalated), disputeId, 4), DisputeStatus.Escalated);
        using HttpClient tool = factory.CreateTool();
        using HttpRequestMessage resolve = OperationsRequest(
            HttpMethod.Post, $"{disputeId}/resolve", factory.Zitadel.Issue(roles: OperationsApi.ResolveRole));
        resolve.Content = new StringContent(OperationsApi.ResolveBody(), Encoding.UTF8, "application/json");

        Assert.True(await hold.SendAndDisconnectAsync(tool, resolve));

        Assert.Equal(2, appendFirst.Appended);
        AuditLogEntry entry = Assert.Single(await factory.AuditRowsAsync(), row => row.ActorType == ActorType.Operations);
        AssertOperationsEntry(entry, AuditAction.Resolve, AuditResult.Rejected, disputeId);
    }

    [Fact]
    public async Task OperationsReadOfAnUnknownDispute_ClientDisconnectsDuringTheAuditSave_CommitsTheRejectedEntry()
    {
        var hold = new HoldAuditSaveInterceptor(AuditAction.DisputeRead);
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub, hold.AddTo);
        using HttpClient tool = factory.CreateTool();
        var unknown = Guid.CreateVersion7();

        Assert.True(await hold.SendAndDisconnectAsync(tool, OperationsRequest(HttpMethod.Get, $"{unknown}", factory.Zitadel.Issue(roles: OperationsApi.ReadRole))));

        AssertOperationsEntry(Assert.Single(await factory.AuditRowsAsync()), AuditAction.DisputeRead, AuditResult.Rejected, unknown);
    }

    /// <summary>A request under <c>/api/v1/operations/disputes/</c> with <paramref name="token"/> as its bearer token.</summary>
    private static HttpRequestMessage OperationsRequest(HttpMethod method, string pathUnderDisputes, string token)
    {
        var request = new HttpRequestMessage(method, new Uri($"{OperationsApi.DisputesPath}/{pathUnderDisputes}", UriKind.Relative));
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        return request;
    }

    /// <summary>A customer POST of <paramref name="json"/> with a fresh antiforgery token.</summary>
    private static async Task<HttpRequestMessage> CustomerRequestAsync(HttpClient browser, string path, string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-XSRF-TOKEN", await browser.AntiforgeryTokenAsync());
        return request;
    }

    private void AssertOperationsEntry(AuditLogEntry entry, AuditAction action, AuditResult result, Guid disputeId)
    {
        Assert.Equal(action, entry.Action);
        Assert.Equal(result, entry.Result);
        Assert.Equal(ActorType.Operations, entry.ActorType);
        Assert.Equal(FakeZitadel.OperationsSub, entry.ActorId);
        Assert.Equal(disputeId, entry.DisputeId);
        Assert.Null(entry.CustomerId);
        Assert.Equal(stub.NowAsStored, entry.OccurredAtUtc);
        Assert.Matches("^[0-9a-f]{32}$", entry.TraceId);
    }

    /// <summary>Another action's stored event at <paramref name="version"/>, as an operations user commits it.</summary>
    private TEvent Next<TEvent>(TEvent storedEvent, Guid disputeId, int version)
        where TEvent : StoredEvent =>
        storedEvent with { DisputeId = disputeId, Version = version, OccurredAtUtc = stub.NowAsStored, ActorType = ActorType.Operations, ActorId = "ops-b" };
}
