using System.Net;
using System.Text.Json;
using DisputeDesk.Api.IntegrationTests.Disputes;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Contracts.Problems;
using DisputeDesk.Domain.Disputes;

namespace DisputeDesk.Api.IntegrationTests.Operations;

/// <summary>
/// AC3 (FR-23, FR-24; AD-8 as amended 2026-10-30): the check order 401, 403, the framework's binding 400 (415 for a body
/// that is not JSON), 404, 400 for a well-formed body, then 409. Binding failures name no field for the body and
/// <c>disputeId</c> for the route value, never echo the input, and are not audited. Start review and escalate ignore any
/// body.
/// </summary>
public sealed class OperationsActionInputTests(OperationsToolFixture api) : IClassFixture<OperationsToolFixture>
{
    private const string Marker = "echo-marker-7f3";

    private ApiFactory Factory => api.Factory;

    private HttpClient Tool => api.Tool;

    private string ResolveToken => Factory.Zitadel.Issue(roles: OperationsApi.ResolveRole);

    /// <summary>Each body that fails to bind; an empty string stands for no body at all.</summary>
    public static string[] BodiesThatDoNotBind =>
    [
        $$"""{"outcome":"upheld","note":"{{Marker}}" """,
        "[]",
        "null",
        "",
        $$"""{"outcome":1,"note":"{{Marker}}"}""",
        $$"""{"outcome":["upheld"],"note":"{{Marker}}"}""",
        $$"""{"outcome":"upheld, rejected","note":"{{Marker}}"}""",
        $$"""{"outcome":"{{Marker}}","note":"Refunded"}""",
        """{"outcome":"upheld","note":5}""",
        // camelCase names only (AC3): the shared converter would read these case-insensitively, as raise's reason does.
        """{"outcome":"Upheld","note":"Refunded"}""",
        """{"outcome":"UPHELD","note":"Refunded"}""",
        """{"outcome":"Rejected","note":"Refunded"}""",
        """{"outcome":"rEjected","note":"Refunded"}""",
    ];

    /// <summary>Each body on an unknown ID (no status), an under-review dispute and a resolved one.</summary>
    public static TheoryData<DisputeStatus?, string> BodiesThatDoNotBindOnEachTarget
    {
        get
        {
            var data = new TheoryData<DisputeStatus?, string>();
            foreach (DisputeStatus? target in new DisputeStatus?[] { null, DisputeStatus.UnderReview, DisputeStatus.Resolved })
            {
                foreach (string body in BodiesThatDoNotBind)
                {
                    data.Add(target, body);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(BodiesThatDoNotBindOnEachTarget))]
    public async Task Resolve_BodyThatDoesNotBind_Returns400WithNoFieldBeforeTheLookupAndWritesNoRow(DisputeStatus? target, string json)
    {
        // A null target is an unknown ID, so a 400 there proves binding answered before the lookup's 404.
        Guid disputeId = target is DisputeStatus status ? await api.DisputeInAsync(status) : Guid.CreateVersion7();

        HttpResponseMessage response = await Tool.ActAsync(disputeId.ToString(), "resolve", ResolveToken, json.Length == 0 ? null : json);

        await response.AssertValidationProblemWithNoFieldAsync();
        Assert.DoesNotContain(Marker, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Empty(await api.AuditRowsAsync(disputeId));
        if (target is DisputeStatus unchanged)
        {
            Assert.Equal(unchanged, (await Factory.DisputeRowAsync(disputeId)).Status);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData(DisputeStatus.UnderReview)]
    [InlineData(DisputeStatus.Resolved)]
    public async Task Resolve_TextPlainBody_Returns415ValidationAndWritesNoRow(DisputeStatus? target)
    {
        Guid disputeId = target is DisputeStatus status ? await api.DisputeInAsync(status) : Guid.CreateVersion7();

        HttpResponseMessage response = await Tool.ActAsync(disputeId.ToString(), "resolve", ResolveToken, OperationsApi.ResolveBody(), "text/plain");

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal(ProblemType.For(CommonProblems.Validation), await response.ProblemTypeAsync());
        Assert.Empty(await api.AuditRowsAsync(disputeId));
    }

    [Fact]
    public async Task Resolve_TextPlainBodyWithNoTokenOrARefusedToken_Returns401BearerOr403Before415()
    {
        Guid disputeId = await api.DisputeInAsync(DisputeStatus.UnderReview);

        HttpResponseMessage anonymous = await Tool.ActAsync(disputeId.ToString(), "resolve", token: null, OperationsApi.ResolveBody(), "text/plain");
        HttpResponseMessage refused = await Tool.ActAsync(disputeId.ToString(), "resolve", Factory.Zitadel.Issue(), OperationsApi.ResolveBody(), "text/plain");

        // The route's own JwtBearer policy answers, never the cookie fallback: the 401 carries the Bearer challenge.
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal("Bearer", Assert.Single(anonymous.Headers.WwwAuthenticate).Scheme);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    public static TheoryData<string> WellFormedBodies => new()
    {
        """{"note":"Refunded"}""",
        """{"outcome":null,"note":"Refunded"}""",
        """{"outcome":"upheld"}""",
        """{"outcome":"upheld","note":"   "}""",
        JsonSerializer.Serialize(new { outcome = "upheld", note = new string('a', 501) }),
        OperationsApi.ResolveBody(),
    };

    [Theory]
    [MemberData(nameof(WellFormedBodies))]
    public async Task Resolve_WellFormedBodyOnAnUnknownId_Returns404NotFound(string json)
    {
        HttpResponseMessage response = await Tool.ActAsync(Guid.CreateVersion7().ToString(), "resolve", ResolveToken, json);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ProblemType.For(CommonProblems.NotFound), await response.ProblemTypeAsync());
    }

    public static TheoryData<DisputeStatus, string, string> BadFieldsOnEachStatus
    {
        get
        {
            var data = new TheoryData<DisputeStatus, string, string>();
            foreach (DisputeStatus status in new[] { DisputeStatus.UnderReview, DisputeStatus.Resolved })
            {
                data.Add(status, """{"note":"Refunded"}""", "outcome");
                data.Add(status, """{"outcome":null,"note":"Refunded"}""", "outcome");
                data.Add(status, """{"outcome":"upheld"}""", "note");
                data.Add(status, """{"outcome":"upheld","note":null}""", "note");
                data.Add(status, """{"outcome":"upheld","note":" \t "}""", "note");
                data.Add(status, JsonSerializer.Serialize(new { outcome = "upheld", note = "  " + new string('a', 501) + "  " }), "note");
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(BadFieldsOnEachStatus))]
    public async Task Resolve_MissingOutcomeOrBadNoteOnAKnownDispute_Returns400NamingTheFieldBeforeTheStatusAndWritesNoRow(DisputeStatus status, string json, string field)
    {
        Guid disputeId = await api.DisputeInAsync(status);

        HttpResponseMessage response = await Tool.ActAsync(disputeId.ToString(), "resolve", ResolveToken, json);

        await response.AssertValidationProblemAsync(field);
        Assert.Empty(await api.AuditRowsAsync(disputeId));
        Assert.Equal(status, (await Factory.DisputeRowAsync(disputeId)).Status);
    }

    [Fact]
    public async Task Resolve_BadNote_ProblemSays500()
    {
        Guid disputeId = await api.DisputeInAsync(DisputeStatus.UnderReview);

        HttpResponseMessage response = await Tool.ActAsync(
            disputeId.ToString(), "resolve", ResolveToken, JsonSerializer.Serialize(new { outcome = "upheld", note = new string('a', 501) }));

        using JsonDocument problem = await response.ReadJsonAsync();
        string? message = problem.RootElement.GetProperty("errors").GetProperty("note")[0].GetString();
        Assert.Equal(DisputeErrors.ResolveNoteInvalid.Message, message);
        Assert.Contains("500", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resolve_ValidBodyWithUnknownPropertiesOnAResolvedDispute_Returns409WithDisputeStatusResolved()
    {
        Guid disputeId = await api.DisputeInAsync(DisputeStatus.Resolved);

        HttpResponseMessage response = await Tool.ActAsync(
            disputeId.ToString(), "resolve", ResolveToken, """{"outcome":"rejected","note":"Again","unknown":42}""");

        await response.AssertNotAllowedInAsync("resolved");
    }

    [Fact]
    public async Task Resolve_ValidBodyWithUnknownPropertiesOnUnderReview_Returns200WithTheTrimmedNote()
    {
        Guid disputeId = await api.DisputeInAsync(DisputeStatus.UnderReview);
        string padded = "   " + new string('a', 500) + "   ";

        HttpResponseMessage response = await Tool.ActAsync(
            disputeId.ToString(), "resolve", ResolveToken, JsonSerializer.Serialize(new { outcome = "rejected", note = padded, unknown = 42 }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = await response.ReadJsonAsync();
        Assert.Equal("rejected", body.RootElement.GetProperty("outcome").GetString());
        Assert.Equal(new string('a', 500), body.RootElement.GetProperty("timeline").EnumerateArray().Last().GetProperty("note").GetString());
    }

    [Theory]
    [MemberData(nameof(OperationsApi.Actions), MemberType = typeof(OperationsApi))]
    public async Task Action_DisputeIdNotAGuid_Returns400NamingDisputeId(string action, string role)
    {
        HttpResponseMessage response = await Tool.ActAsync("not-a-guid", action, Factory.Zitadel.Issue(roles: role), OperationsApi.BodyFor(action));

        await response.AssertValidationProblemAsync("disputeId");
        Assert.DoesNotContain("not-a-guid", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("start-review", DisputeStatus.Submitted)]
    [InlineData("escalate", DisputeStatus.UnderReview)]
    public async Task StartReviewOrEscalate_AnyBody_IsIgnored(string action, DisputeStatus from)
    {
        Guid disputeId = await api.DisputeInAsync(from);

        HttpResponseMessage response = await Tool.ActAsync(
            disputeId.ToString(), action, Factory.Zitadel.Issue(roles: OperationsApi.RoleOf(action)), "not json {", "text/plain");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("start-review")]
    [InlineData("escalate")]
    public async Task StartReviewOrEscalate_UnknownId_Returns404(string action)
    {
        HttpResponseMessage response = await Tool.ActAsync(Guid.CreateVersion7().ToString(), action, Factory.Zitadel.Issue(roles: OperationsApi.RoleOf(action)));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ProblemType.For(CommonProblems.NotFound), await response.ProblemTypeAsync());
    }
}
