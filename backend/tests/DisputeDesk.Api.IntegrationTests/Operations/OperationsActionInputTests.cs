using System.Net;
using System.Text.Json;
using DisputeDesk.Api.IntegrationTests.Disputes;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Contracts.Audit;
using DisputeDesk.Contracts.Problems;
using DisputeDesk.Domain.Disputes;

namespace DisputeDesk.Api.IntegrationTests.Operations;

/// <summary>
/// AC3 (FR-23, FR-24; AD-8): the check order 401, 403, 400 (the framework's binding, then the built-in validation of the
/// outcome and the note), 404, then 409. Binding failures name no field for the body and <c>disputeId</c> for the route
/// value, validation errors name their field, neither echoes the input, and neither is audited. A body that is not JSON
/// is answered by the cookie fallback policy (human decision 2026-11-04). Start review and escalate ignore any body.
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
    [InlineData("none")]
    [InlineData("refused")]
    [InlineData("resolve role")]
    public async Task Resolve_TextPlainBodyWithAnyToken_Returns401FromTheCookieFallbackAndWritesNoRow(string token)
    {
        Guid disputeId = await api.DisputeInAsync(DisputeStatus.UnderReview);
        int storedEvents = (await Factory.StreamAsync(disputeId)).Count;
        int outboxRows = (await Factory.OutboxRowsAsync(disputeId)).Count;
        string? bearer = token switch
        {
            "none" => null,
            "refused" => Factory.Zitadel.Issue(),
            _ => ResolveToken,
        };

        HttpResponseMessage response = await Tool.ActAsync(disputeId.ToString(), "resolve", bearer, OperationsApi.ResolveBody(), "text/plain");

        // Routing's 415 endpoint carries no bearer policy, so the cookie fallback answers, and a cookie 401 has no challenge.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ProblemType.For(CommonProblems.Unauthorized), await response.ProblemTypeAsync());
        Assert.Empty(response.Headers.WwwAuthenticate);
        // No Denied entry for the refused token either (AD-14a, the unaudited case).
        Assert.Empty(await api.AuditRowsAsync(disputeId));
        Assert.Equal(DisputeStatus.UnderReview, (await Factory.DisputeRowAsync(disputeId)).Status);
        Assert.Equal(storedEvents, (await Factory.StreamAsync(disputeId)).Count);
        Assert.Equal(outboxRows, (await Factory.OutboxRowsAsync(disputeId)).Count);
    }

    [Fact]
    public async Task Resolve_NoOutcomeWithNoTokenOrARefusedToken_Returns401BearerOr403WithItsDeniedEntryBefore400()
    {
        Guid disputeId = await api.DisputeInAsync(DisputeStatus.UnderReview);
        const string NoOutcome = """{"note":"Refunded"}""";

        HttpResponseMessage anonymous = await Tool.ActAsync(disputeId.ToString(), "resolve", token: null, NoOutcome);
        HttpResponseMessage refused = await Tool.ActAsync(disputeId.ToString(), "resolve", Factory.Zitadel.Issue(), NoOutcome);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal("Bearer", Assert.Single(anonymous.Headers.WwwAuthenticate).Scheme);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(ProblemType.For(CommonProblems.Forbidden), await refused.ProblemTypeAsync());
        Assert.Equal(AuditResult.Denied, Assert.Single(await api.AuditRowsAsync(disputeId)).Result);
    }

    /// <summary>Valid bodies, the note at each end of 1 to 500 units after trimming.</summary>
    public static TheoryData<string> WellFormedBodies => new()
    {
        """{"outcome":"upheld","note":" a "}""",
        JsonSerializer.Serialize(new { outcome = "rejected", note = "  " + new string('a', 500) + "  " }),
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

    /// <summary>
    /// Each body that binds but breaks a rule, with the field it names, on an unknown ID (no status), an under-review
    /// dispute and a resolved one.
    /// </summary>
    public static TheoryData<DisputeStatus?, string, string> BadFieldsOnEachTarget
    {
        get
        {
            var data = new TheoryData<DisputeStatus?, string, string>();
            foreach (DisputeStatus? status in new DisputeStatus?[] { null, DisputeStatus.UnderReview, DisputeStatus.Resolved })
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
    [MemberData(nameof(BadFieldsOnEachTarget))]
    public async Task Resolve_MissingOutcomeOrBadNote_Returns400NamingTheFieldBeforeTheLookupAndWritesNoRow(DisputeStatus? target, string json, string field)
    {
        // A null target is an unknown ID, so a 400 there proves the validation answered before the lookup's 404.
        Guid disputeId = target is DisputeStatus status ? await api.DisputeInAsync(status) : Guid.CreateVersion7();

        HttpResponseMessage response = await Tool.ActAsync(disputeId.ToString(), "resolve", ResolveToken, json);

        await response.AssertValidationProblemAsync(field);
        Assert.Empty(await api.AuditRowsAsync(disputeId));
        if (target is DisputeStatus unchanged)
        {
            Assert.Equal(unchanged, (await Factory.DisputeRowAsync(disputeId)).Status);
        }
    }

    /// <summary>A note missing, only whitespace, or 501 units after trimming.</summary>
    public static TheoryData<string> BadNotes => new()
    {
        """{"outcome":"upheld"}""",
        """{"outcome":"upheld","note":" \t "}""",
        JsonSerializer.Serialize(new { outcome = "upheld", note = new string('a', 501) }),
    };

    [Theory]
    [MemberData(nameof(BadNotes))]
    public async Task Resolve_BadNote_ProblemSays500(string json)
    {
        Guid disputeId = await api.DisputeInAsync(DisputeStatus.UnderReview);

        HttpResponseMessage response = await Tool.ActAsync(disputeId.ToString(), "resolve", ResolveToken, json);

        using JsonDocument problem = await response.ReadJsonAsync();
        string? message = problem.RootElement.GetProperty("errors").GetProperty("note")[0].GetString();
        Assert.Equal(DisputeErrors.ResolveNoteInvalid.Message, message);
        Assert.Contains("500", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"note":"Refunded"}""")]
    [InlineData("""{"outcome":null,"note":"Refunded"}""")]
    public async Task Resolve_NoOutcome_ProblemSaysUpheldOrRejected(string json)
    {
        HttpResponseMessage response = await Tool.ActAsync(Guid.CreateVersion7().ToString(), "resolve", ResolveToken, json);

        using JsonDocument problem = await response.ReadJsonAsync();
        string? message = problem.RootElement.GetProperty("errors").GetProperty("outcome")[0].GetString();
        Assert.Equal("An outcome is required: upheld or rejected.", message);
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

    [Fact]
    public async Task Resolve_DisputeIdNotAGuidAndNoOutcome_Returns400NamingOutcome()
    {
        // The built-in validation still validates the bound body when the route value fails to bind, and runs before
        // the group's filter that names disputeId; both are 400 before the lookup (AD-8).
        HttpResponseMessage response = await Tool.ActAsync("not-a-guid", "resolve", ResolveToken, """{"note":"Refunded"}""");

        await response.AssertValidationProblemAsync("outcome");
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
