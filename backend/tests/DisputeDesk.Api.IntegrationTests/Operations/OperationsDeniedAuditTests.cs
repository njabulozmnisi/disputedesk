using System.Net;
using System.Text.Json;
using DisputeDesk.Api.IntegrationTests.Disputes;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Application.Audit;
using DisputeDesk.Contracts.Audit;
using DisputeDesk.Contracts.Problems;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace DisputeDesk.Api.IntegrationTests.Operations;

/// <summary>
/// AC2 and AC5 (FR-24, FR-25; AD-9, AD-14a): on every operations route each valid token that may not act gets 403
/// <c>forbidden</c> with the same body for a real and an unknown ID, before any lookup, and the one authorization result
/// handler records exactly one <c>Denied</c> row for it, saved before the 403: the route's action, actor type
/// <c>Operations</c>, the bearer token's <c>sub</c> mapped or not, the route's dispute ID when it is a GUID, and no
/// customer ID. A 401, and every customer or linking 403, writes no row; a failed save is a 500.
/// </summary>
public class OperationsDeniedAuditTests(PostgresFixture postgres, TransactionsStub stub) : IClassFixture<TransactionsStub>
{
    private const string CustomerLogin = "login-a";

    /// <summary>Each route (the read and the three actions) with each token that may not use it.</summary>
    public static TheoryData<string, string> RefusedTokens
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (string route in new[] { "read", "start-review", "escalate", "resolve" })
            {
                foreach (string token in new[] { "no role", "dispute.read only", "another action's role", "own role, unmapped sub", "customer ID token with the own role" })
                {
                    if (route == "read" && token == "dispute.read only")
                    {
                        continue; // the read's own role
                    }

                    data.Add(route, token);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(RefusedTokens))]
    public async Task Route_RefusedToken_Returns403SameBodyForARealAndAnUnknownIdAndWritesOneDeniedRowEach(string route, string token)
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        Guid disputeId = await factory.RaiseForCustomerAAsync();
        // Under review, so each action would succeed with its own role and a mapped sub.
        Assert.Equal(HttpStatusCode.OK, (await factory.ActAsMappedAsync(factory.CreateTool(), disputeId, "start-review")).StatusCode);
        using HttpClient tool = factory.CreateTool();
        (string signed, string sub) = Token(factory.Zitadel, route, token);
        var unknown = Guid.CreateVersion7();

        HttpResponseMessage real = await SendAsync(tool, route, disputeId.ToString(), signed);
        HttpResponseMessage other = await SendAsync(tool, route, unknown.ToString(), signed);

        Assert.Equal(HttpStatusCode.Forbidden, real.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, other.StatusCode);
        Assert.Equal(ProblemType.For(CommonProblems.Forbidden), await real.ProblemTypeAsync());
        Assert.Equal(await real.BodyWithoutTraceIdAsync(), await other.BodyWithoutTraceIdAsync());
        // The clock is fixed, so the rows are told apart by their dispute ID, not by their order.
        List<AuditLogEntry> denied = await DeniedRowsAsync(factory);
        Assert.Equal(2, denied.Count);
        AuditLogEntry forReal = Assert.Single(denied, entry => entry.DisputeId == disputeId);
        AuditLogEntry forUnknown = Assert.Single(denied, entry => entry.DisputeId == unknown);
        AssertDenied(forReal, ActionOf(route), sub, disputeId);
        AssertDenied(forUnknown, ActionOf(route), sub, unknown);
        // Equal apart from Id, DisputeId, OccurredAtUtc and TraceId.
        Assert.Equal(OtherColumns(forReal), OtherColumns(forUnknown));
        Assert.Equal(DisputeStatus.UnderReview, (await factory.DisputeRowAsync(disputeId)).Status);
    }

    [Theory]
    [InlineData("read")]
    [InlineData("start-review")]
    [InlineData("escalate")]
    [InlineData("resolve")]
    public async Task Route_RefusedTokenAndDisputeIdNotAGuid_Returns403AndWritesOneDeniedRowWithNoDisputeId(string route)
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient tool = factory.CreateTool();

        HttpResponseMessage response = await SendAsync(tool, route, "not-a-guid", factory.Zitadel.Issue());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        AssertDenied(Assert.Single(await factory.AuditRowsAsync()), ActionOf(route), FakeZitadel.OperationsSub, disputeId: null);
    }

    [Theory]
    [InlineData("start-review")]
    [InlineData("escalate")]
    [InlineData("resolve")]
    public async Task Action_SessionCookieAndAnUnprivilegedBearerToken_RecordsTheBearersSubNeverTheCustomers(string action)
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        Guid disputeId = await factory.RaiseForCustomerAAsync();
        using HttpClient browser = await SignedInAsCustomerAAsync(factory);

        HttpResponseMessage response = await browser.ActAsync(disputeId.ToString(), action, factory.Zitadel.Issue(), OperationsApi.BodyFor(action));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        AuditLogEntry denied = Assert.Single(await DeniedRowsAsync(factory));
        AssertDenied(denied, ActionOf(action), FakeZitadel.OperationsSub, disputeId);
        Assert.NotEqual(CustomerLogin, denied.ActorId);
    }

    [Theory]
    [InlineData("start-review")]
    [InlineData("escalate")]
    [InlineData("resolve")]
    public async Task Action_NoCredentialOrTheSessionCookieWithAValidAntiforgeryHeader_Returns401AndWritesNoRow(string action)
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        Guid disputeId = await factory.RaiseForCustomerAAsync();
        using HttpClient tool = factory.CreateTool();
        using HttpClient browser = await SignedInAsCustomerAAsync(factory);
        string antiforgery = await browser.AntiforgeryTokenAsync();
        List<AuditLogEntry> before = await factory.AuditRowsAsync();

        HttpResponseMessage anonymous = await tool.ActAsync(disputeId.ToString(), action, token: null, OperationsApi.BodyFor(action));
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"{OperationsApi.DisputesPath}/{disputeId}/{action}", UriKind.Relative))
        {
            Content = OperationsApi.BodyFor(action) is string json ? new StringContent(json, System.Text.Encoding.UTF8, "application/json") : null,
        };
        request.Headers.Add("X-XSRF-TOKEN", antiforgery);
        HttpResponseMessage cookie = await browser.SendAsync(request, TestContext.Current.CancellationToken);

        // The cookie never authenticates an operations route, so a forged browser request cannot act (OWASP CSRF
        // Prevention Cheat Sheet: a browser never adds an Authorization header by itself).
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, cookie.StatusCode);
        Assert.Equal("Bearer", Assert.Single(cookie.Headers.WwwAuthenticate).Scheme);
        Assert.Equal(before.Count, (await factory.AuditRowsAsync()).Count);
        Assert.Equal(DisputeStatus.Submitted, (await factory.DisputeRowAsync(disputeId)).Status);
    }

    [Fact]
    public async Task CustomerRoute_UnlinkedLogin_StillGets403LinkingRequiredAndWritesNoRow()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        Guid disputeId = await factory.RaiseForCustomerAAsync();
        using HttpClient browser = factory.CreateBrowser(new CookieContainer());
        await browser.SignInAsync(factory.Zitadel, "unlinked-login");
        List<AuditLogEntry> before = await factory.AuditRowsAsync();

        HttpResponseMessage withdraw = await browser.WithdrawAsync(disputeId.ToString());
        HttpResponseMessage detail = await browser.GetDisputeAsync(disputeId.ToString());

        Assert.Equal(HttpStatusCode.Forbidden, withdraw.StatusCode);
        Assert.Equal(ProblemType.For(LinkingProblems.LinkingRequired), await withdraw.ProblemTypeAsync());
        Assert.Equal(HttpStatusCode.Forbidden, detail.StatusCode);
        Assert.Equal(ProblemType.For(LinkingProblems.LinkingRequired), await detail.ProblemTypeAsync());
        Assert.Equal(before.Count, (await factory.AuditRowsAsync()).Count);
    }

    [Theory]
    [InlineData("read")]
    [InlineData("start-review")]
    [InlineData("escalate")]
    [InlineData("resolve")]
    public async Task Route_DeniedSaveFails_Returns500InternalWithNoDisputeData(string route)
    {
        var failAudit = new FailAuditInsertInterceptor { Armed = false };
        await using ApiFactory factory = await TransactionsApi.StartAsync(
            postgres, stub, services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(failAudit)));
        Guid disputeId = await factory.RaiseForCustomerAAsync();
        using HttpClient tool = factory.CreateTool();
        failAudit.Armed = true;

        HttpResponseMessage response = await SendAsync(tool, route, disputeId.ToString(), factory.Zitadel.Issue());

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(1, failAudit.Failed);
        using JsonDocument problem = await response.ReadJsonAsync();
        Assert.Equal(ProblemType.For(CommonProblems.Internal), problem.RootElement.GetProperty("type").GetString());
        Assert.Equal(["type", "title", "status", "traceId"], OperationsApi.Names(problem.RootElement));
        Assert.DoesNotContain(await factory.AuditRowsAsync(), entry => entry.Result == AuditResult.Denied);
    }

    private static async Task<List<AuditLogEntry>> DeniedRowsAsync(ApiFactory factory) =>
        [.. (await factory.AuditRowsAsync()).Where(entry => entry.Result == AuditResult.Denied)];

    /// <summary>Customer A's browser, signed in; <see cref="OperationsApi.RaiseForCustomerAAsync"/> already linked the login.</summary>
    private static async Task<HttpClient> SignedInAsCustomerAAsync(ApiFactory factory)
    {
        HttpClient browser = factory.CreateBrowser(new CookieContainer());
        await browser.SignInAsync(factory.Zitadel, CustomerLogin);
        return browser;
    }

    /// <summary>Every column but <c>Id</c>, <c>DisputeId</c>, <c>OccurredAtUtc</c> and <c>TraceId</c>.</summary>
    private static (AuditAction, AuditResult, ActorType, string, string?) OtherColumns(AuditLogEntry entry) =>
        (entry.Action, entry.Result, entry.ActorType, entry.ActorId, entry.CustomerId);

    private static Task<HttpResponseMessage> SendAsync(HttpClient tool, string route, string disputeId, string token) =>
        route == "read"
            ? tool.ReadDisputeAsync(disputeId, token)
            : tool.ActAsync(disputeId, route, token, OperationsApi.BodyFor(route));

    private static AuditAction ActionOf(string route) => route switch
    {
        "read" => AuditAction.DisputeRead,
        "start-review" => AuditAction.StartReview,
        "escalate" => AuditAction.Escalate,
        "resolve" => AuditAction.Resolve,
        _ => throw new ArgumentOutOfRangeException(nameof(route), route, "Unknown route."),
    };

    private static string RoleOf(string route) => route == "read" ? OperationsApi.ReadRole : OperationsApi.RoleOf(route);

    /// <summary>The signed token for one case, and the <c>sub</c> it carries.</summary>
    private static (string Signed, string Sub) Token(FakeZitadel zitadel, string route, string token)
    {
        switch (token)
        {
            case "no role":
                return (zitadel.Issue(), FakeZitadel.OperationsSub);
            case "dispute.read only":
                return (zitadel.Issue(roles: OperationsApi.ReadRole), FakeZitadel.OperationsSub);
            case "another action's role":
                string another = route == "resolve" ? OperationsApi.StartReviewRole : OperationsApi.ResolveRole;
                return (zitadel.Issue(roles: another), FakeZitadel.OperationsSub);
            case "own role, unmapped sub":
                return (zitadel.Issue("unmapped-machine-user", RoleOf(route)), "unmapped-machine-user");
            case "customer ID token with the own role":
                return (FakeZitadel.Sign(CustomerIdToken(zitadel, RoleOf(route))), CustomerLogin);
            default:
                throw new ArgumentOutOfRangeException(nameof(token), token, "Unknown case.");
        }
    }

    /// <summary>A token shaped like the BFF's ID token for customer A, as in <see cref="OperationsRolesTests"/>.</summary>
    private static SecurityTokenDescriptor CustomerIdToken(FakeZitadel zitadel, params string[] roles)
    {
        SecurityTokenDescriptor idToken = zitadel.AccessToken(CustomerLogin, roles);
        idToken.Audience = null;
        idToken.Claims["aud"] = new[] { FakeZitadel.ClientId, FakeZitadel.ProjectId };
        idToken.Claims["azp"] = FakeZitadel.ClientId;
        idToken.Claims["email"] = $"{CustomerLogin}@disputedesk.test";
        idToken.Claims["email_verified"] = true;
        idToken.Claims["nonce"] = "nonce-test";
        return idToken;
    }

    private void AssertDenied(AuditLogEntry entry, AuditAction action, string sub, Guid? disputeId)
    {
        Assert.Equal(action, entry.Action);
        Assert.Equal(AuditResult.Denied, entry.Result);
        Assert.Equal(ActorType.Operations, entry.ActorType);
        Assert.Equal(sub, entry.ActorId);
        Assert.Equal(disputeId, entry.DisputeId);
        Assert.Null(entry.CustomerId);
        Assert.Equal(stub.Now, entry.OccurredAtUtc, TimeSpan.FromMilliseconds(1));
        Assert.Matches("^[0-9a-f]{32}$", entry.TraceId);
    }
}
