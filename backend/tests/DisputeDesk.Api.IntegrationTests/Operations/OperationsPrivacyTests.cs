using System.Diagnostics;
using System.Net;
using DisputeDesk.Api.IntegrationTests.Disputes;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Application.Disputes;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Logging;
using OpenTelemetry.Logs;

namespace DisputeDesk.Api.IntegrationTests.Operations;

/// <summary>
/// AC5, FR-30 and NFR-SEC-6: the operations read returns nothing personal beyond the stored snapshot. The body holds no
/// customer ID, login, operations <c>sub</c>, actor ID, trace ID or note the customer wrote (ASVS 5.0 V14.2.6), and the
/// read's spans and log lines hold no customer ID, account reference or <c>Authorization</c> value (AD-20).
/// </summary>
public class OperationsPrivacyTests(PostgresFixture postgres, TransactionsStub stub) : IClassFixture<TransactionsStub>
{
    private const string LoginSub = "login-a";
    private const string RaiseNote = "raise-note-marker-41c7";
    private const string WithdrawNote = "withdraw-note-marker-8e2d";

    [Fact]
    public async Task ReadDispute_DisputeWithCustomerNotes_BodyHoldsNoIdentityTraceOrCustomerNote()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        Guid disputeId = await RaiseAndWithdrawAsync(factory);
        (string[] actorIds, string[] traceIds) = await StreamIdentitiesAsync(factory, disputeId);
        Assert.Equal([LoginSub, LoginSub], actorIds);
        Assert.Equal(2, traceIds.Length);
        using HttpClient tool = factory.CreateTool();

        HttpResponseMessage response = await tool.ReadDisputeAsync(disputeId.ToString(), factory.Zitadel.Issue(roles: OperationsApi.ReadRole));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        string[] forbidden = [TransactionsApi.CustomerA, LoginSub, FakeZitadel.OperationsSub, .. actorIds, .. traceIds, RaiseNote, WithdrawNote];
        Assert.All(forbidden, value => Assert.DoesNotContain(value, body, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ReadDispute_Traced_ExportsNoCustomerIdAccountReferenceOrAuthorizationValue()
    {
        var telemetry = new TelemetryCapture();
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub, telemetry.AddTo);
        Guid disputeId = await RaiseAndWithdrawAsync(factory);
        using HttpClient tool = factory.CreateTool();
        string token = factory.Zitadel.Issue(roles: OperationsApi.ReadRole);
        // A trace ID of the test's own, so spans from tests running in parallel are never mistaken for this request's.
        var traceId = ActivityTraceId.CreateRandom();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"{OperationsApi.DisputesPath}/{disputeId}", UriKind.Relative));
        request.Headers.Add("Authorization", $"Bearer {token}");
        request.Headers.Add("traceparent", $"00-{traceId.ToHexString()}-{ActivitySpanId.CreateRandom().ToHexString()}-01");

        HttpResponseMessage response = await tool.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await telemetry.WaitForSpanAsync(span => span.TraceId == traceId && span.Kind == ActivityKind.Server);
        IReadOnlyList<Activity> spans = telemetry.Spans(traceId);
        Assert.Contains(spans, span => span.Source.Name == "Npgsql");
        // The signature alone is enough to recognise the token in any text.
        string[] forbidden = [TransactionsApi.CustomerA, DisputesApi.AccountA, token.Split('.')[2]];
        Assert.All(spans, span => AssertNoneOf(forbidden, SpanTexts(span)));
        // Every line this Api logged in the test, the sign-in and the raise included; the read itself logs nothing at the
        // configured levels.
        Assert.NotEmpty(telemetry.Logs);
        Assert.All(telemetry.Logs, log => AssertNoneOf(forbidden, LogTexts(log)));
        // IdentityModel would otherwise write token contents into its exception messages and logs.
        Assert.False(IdentityModelEventSource.ShowPII);
    }

    /// <summary>Customer A raises a dispute with a note, then a withdraw with a note is appended to its stream.</summary>
    private async Task<Guid> RaiseAndWithdrawAsync(ApiFactory factory)
    {
        Guid disputeId = await factory.RaiseForCustomerAAsync(RaiseNote);
        await factory.AppendAsync(disputeId, DisputeStatus.Withdrawn, outcome: null, new DisputeWithdrawn(DisputeStatus.Submitted, DisputeStatus.Withdrawn, WithdrawNote)
        {
            Version = 2,
            OccurredAtUtc = stub.NowAsStored.AddHours(1),
            ActorType = ActorType.Customer,
            ActorId = LoginSub,
            TraceId = ActivityTraceId.CreateRandom().ToHexString(),
        });
        return disputeId;
    }

    /// <summary>The actor IDs and trace IDs the dispute's stored events hold, read past the ownership filter.</summary>
    private static async Task<(string[] ActorIds, string[] TraceIds)> StreamIdentitiesAsync(ApiFactory factory, Guid disputeId)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        List<StoredEventRow> rows = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<StoredEventRow>()
            .IgnoreQueryFilters()
            .Where(row => row.DisputeId == disputeId)
            .OrderBy(row => row.Version)
            .ToListAsync(TestContext.Current.CancellationToken);
        return ([.. rows.Select(row => row.ActorId)], [.. rows.Select(row => row.TraceId).OfType<string>()]);
    }

    private static IEnumerable<string?> SpanTexts(Activity span) =>
    [
        span.DisplayName,
        span.StatusDescription,
        .. span.TagObjects.Select(tag => tag.Value?.ToString()),
        .. span.Events.SelectMany(spanEvent => spanEvent.Tags.Select(tag => tag.Value?.ToString())),
    ];

    private static IEnumerable<string?> LogTexts(LogRecord log) =>
    [
        log.FormattedMessage,
        log.Body,
        log.Exception?.ToString(),
        .. (log.Attributes ?? []).Select(attribute => attribute.Value?.ToString()),
    ];

    private static void AssertNoneOf(string[] forbidden, IEnumerable<string?> texts)
    {
        foreach (string text in texts.OfType<string>())
        {
            Assert.All(forbidden, value => Assert.DoesNotContain(value, text, StringComparison.Ordinal));
        }
    }
}
