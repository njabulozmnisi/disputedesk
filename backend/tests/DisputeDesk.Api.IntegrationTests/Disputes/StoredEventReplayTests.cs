using System.Diagnostics;
using System.Net;
using System.Text.Json;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Application.Abstractions;
using DisputeDesk.Application.Disputes;
using DisputeDesk.Contracts.Json;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Domain.Transactions;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests.Disputes;

/// <summary>
/// Loading by replay (AD-13) on real Postgres: through the ownership filter, refusing a row of an unknown type, and
/// reproducing each committed dispute's read model row and the timeline the raise answered with.
/// </summary>
public class StoredEventReplayTests(PostgresFixture postgres, TransactionsStub stub) : IClassFixture<TransactionsStub>
{
    [Theory]
    [MemberData(nameof(PublishMappingTests.OneOfEachType), MemberType = typeof(PublishMappingTests))]
    public void StoredEventRow_EachType_ReadsBackAsTheStoredEventItHolds(StoredEvent storedEvent, string? _)
    {
        // Every type the Domain declares (PublishMappingTests covers them all): its columns and its Data together.
        Dispute owner = Dispute.Raise(
            TransactionsApi.CustomerA,
            new TransactionSnapshot("ACC-A-1", "TXN-A-0029", "0011", stub.Now.AddDays(-2), "Shop", 10m, "ZAR", null,
                TransactionDirection.Debit, TransactionKind.Card, TransactionState.Settled),
            everDisputed: false, Reason.Unrecognised, note: null, expectedAmount: null, new Actor(ActorType.Customer, "login-a"), stub.Now).Value;

        string traceId = ActivityTraceId.CreateRandom().ToHexString();

        var row = StoredEventRow.From(Guid.CreateVersion7(stub.Now), owner, storedEvent, traceId);

        Assert.Equal(storedEvent.GetType().Name, row.EventType);
        Assert.Equal(storedEvent with { TraceId = traceId }, row.ToStoredEvent());
    }

    [Fact]
    public async Task LoadDispute_OwnOthersAndUnknown_ReturnsOnlyTheOwnDisputeAtTheStateOfItsStream()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        Guid mine = await RaiseAsync(factory, "login-a", TransactionsApi.CustomerA, DisputesApi.Body(note: "Not me"));
        Guid theirs = await RaiseAsync(factory, "login-b", TransactionsApi.CustomerB, DisputesApi.Body("TXN-B-0027", "ACC-B-1"));
        await using AsyncServiceScope scope = ScopeFor(factory, TransactionsApi.CustomerA);
        IAppDbContext db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        Dispute? own = await db.LoadDisputeAsync(mine, cancellationToken);

        Assert.NotNull(own);
        Assert.Equal(mine, own.Id);
        Assert.Equal(DisputeStatus.Submitted, own.Status);
        Assert.Equal(1, own.Version);
        Assert.Equal("Not me", own.Note);
        Assert.Null(await db.LoadDisputeAsync(theirs, cancellationToken));
        Assert.Null(await db.LoadDisputeAsync(Guid.CreateVersion7(stub.Now), cancellationToken));
    }

    [Fact]
    public async Task LoadDispute_RowOfAnUnknownEventType_ThrowsRatherThanReturnADisputeOrNotFound()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        Guid mine = await RaiseAsync(factory, "login-a", TransactionsApi.CustomerA, DisputesApi.Body());
        await using (AsyncServiceScope writer = factory.Services.CreateAsyncScope())
        {
            AppDbContext db = writer.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Set<StoredEventRow>().Add(new StoredEventRow
            {
                Id = Guid.CreateVersion7(stub.Now),
                DisputeId = mine,
                Version = 2,
                EventType = "DisputeReopened",
                OccurredAtUtc = stub.Now,
                ActorType = ActorType.Customer,
                ActorId = "login-a",
                Data = "{}",
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using AsyncServiceScope scope = ScopeFor(factory, TransactionsApi.CustomerA);
        IAppDbContext reader = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.LoadDisputeAsync(mine, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Replay_EveryRaisedDispute_ReproducesItsDisputesRowAndTheTimelineTheApiReturned()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient a = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        using HttpClient b = await factory.SignInLinkedAsync("login-b", TransactionsApi.CustomerB);
        // A spread of raise inputs: a note, a wrong amount, no note, and another customer.
        List<JsonElement> created =
        [
            await CreatedAsync(a, DisputesApi.Body(note: "  I never bought this  ")),
            await CreatedAsync(a, DisputesApi.Body("TXN-A-0028", reason: "wrongAmount", expectedAmount: "200.70")),
            await CreatedAsync(a, DisputesApi.Body("TXN-A-0027", reason: "duplicateCharge")),
            await CreatedAsync(b, DisputesApi.Body("TXN-B-0027", "ACC-B-1", reason: "notReceived", note: "Never arrived")),
        ];
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        IEntityType disputeType = db.Model.FindEntityType(typeof(Dispute)) ?? throw new InvalidOperationException("Dispute is not mapped.");

        foreach (JsonElement body in created)
        {
            Guid disputeId = body.GetProperty("disputeId").GetGuid();
            List<StoredEventRow> rows = await db.Set<StoredEventRow>().IgnoreQueryFilters().AsNoTracking()
                .Where(row => row.DisputeId == disputeId).OrderBy(row => row.Version).ToListAsync(TestContext.Current.CancellationToken);
            List<StoredEvent> stream = [.. rows.Select(row => row.ToStoredEvent())];
            Dispute row = await db.Set<Dispute>().IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(dispute => dispute.Id == disputeId, TestContext.Current.CancellationToken);

            var replayed = Dispute.Replay(stream);

            Assert.All(disputeType.GetProperties(), column =>
                Assert.Equal(column.PropertyInfo?.GetValue(row), column.PropertyInfo?.GetValue(replayed)));
            AssertSameTimeline(body.GetProperty("timeline"), CustomerView.ToTimeline(stream));
        }
    }

    /// <summary>The timeline built from the stored stream, item for item and in order, as the raise answered it.</summary>
    private static void AssertSameTimeline(JsonElement answered, IReadOnlyList<Contracts.Disputes.TimelineEntryResponse> built)
    {
        using JsonDocument rebuilt = JsonSerializer.SerializeToDocument(built, DisputeDeskJson.Options);
        List<JsonElement> expected = [.. answered.EnumerateArray()];
        List<JsonElement> actual = [.. rebuilt.RootElement.EnumerateArray()];
        Assert.Equal(expected.Count, actual.Count);
        for (int index = 0; index < expected.Count; index++)
        {
            foreach (string field in new[] { "action", "customerStatus", "actor", "note" })
            {
                Assert.Equal(expected[index].GetProperty(field).ToString(), actual[index].GetProperty(field).ToString());
            }

            // The clock reads a tick finer than Postgres keeps, so this holds only if the raise answered the stored instant.
            Assert.Equal(expected[index].GetProperty("occurredAt").GetDateTimeOffset(), actual[index].GetProperty("occurredAt").GetDateTimeOffset());
        }
    }

    private static async Task<JsonElement> CreatedAsync(HttpClient browser, string body)
    {
        HttpResponseMessage response = await browser.RaiseAsync(body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using JsonDocument created = await response.ReadJsonAsync();
        return created.RootElement.Clone();
    }

    private static async Task<Guid> RaiseAsync(ApiFactory factory, string loginSub, string customerId, string body)
    {
        using HttpClient browser = await factory.SignInLinkedAsync(loginSub, customerId);
        return (await CreatedAsync(browser, body)).GetProperty("disputeId").GetGuid();
    }

    /// <summary>A request scope whose current customer is <paramref name="customerId"/>, as the Linked policy sets it.</summary>
    private static AsyncServiceScope ScopeFor(ApiFactory factory, string customerId)
    {
        AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentCustomer>().Set(customerId, "login-" + customerId);
        return scope;
    }
}
