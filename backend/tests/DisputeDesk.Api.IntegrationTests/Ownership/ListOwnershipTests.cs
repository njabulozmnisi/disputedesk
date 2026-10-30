using System.Net;
using System.Text.Json;
using DisputeDesk.Api.IntegrationTests.Disputes;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;

namespace DisputeDesk.Api.IntegrationTests.Ownership;

/// <summary>
/// Story 3.9 AC3 (AD-11; ASVS 5.0 V8.2.2): customer A's list holds only A's disputes on every page, whatever the
/// cursor names and whatever customer ID the request carries. A and B's disputes interleave by <c>CreatedAtUtc</c>, and
/// one pair shares an instant. The cursor is a position, not a capability: ownership comes only from the filter.
/// </summary>
public sealed class ListOwnershipTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory? _factory;
    private HttpClient? _browser;
    private List<SeededDispute> _seeded = [];

    private HttpClient Browser => _browser ?? throw new InvalidOperationException("Not initialised.");

    private List<SeededDispute> DisputesOfA => DisputesApi.NewestFirst(_seeded.Where(dispute => dispute.CustomerId == TransactionsApi.CustomerA));

    private SeededDispute DisputeOfBAtItsOwnInstant => _seeded.Single(dispute => dispute.CustomerId == TransactionsApi.CustomerB && dispute.CreatedAt == DisputesApi.SeedInstant.AddHours(-1));

    private SeededDispute DisputeOfBSharingAnInstant => _seeded.Single(dispute => dispute.CustomerId == TransactionsApi.CustomerB && dispute.CreatedAt == DisputesApi.SeedInstant.AddHours(-2));

    public async ValueTask InitializeAsync()
    {
        _factory = await DisputesApi.StartWithoutCoreBankingAsync(postgres);
        _browser = await _factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        DateTimeOffset at = DisputesApi.SeedInstant;
        _seeded =
        [
            await _factory.SeedDisputeAsync(TransactionsApi.CustomerA, "TXN-A-1", at),
            await _factory.SeedDisputeAsync(TransactionsApi.CustomerB, "TXN-B-1", at.AddHours(-1)),
            await _factory.SeedDisputeAsync(TransactionsApi.CustomerA, "TXN-A-2", at.AddHours(-2)),
            await _factory.SeedDisputeAsync(TransactionsApi.CustomerB, "TXN-B-2", at.AddHours(-2)),
            await _factory.SeedDisputeAsync(TransactionsApi.CustomerB, "TXN-B-3", at.AddHours(-3)),
            await _factory.SeedDisputeAsync(TransactionsApi.CustomerA, "TXN-A-3", at.AddHours(-4)),
        ];
    }

    public async ValueTask DisposeAsync()
    {
        _browser?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    [Theory]
    [InlineData("limit=1")]
    [InlineData("limit=2")]
    public async Task ListDisputes_EveryPage_HoldsOnlyTheCallersDisputesAndEachCursorNamesTheirLastItem(string limitQuery)
    {
        // ListEveryDisputeAsync checks that each nextCursor decodes to the page's last item, which is A's, so B's rows
        // never move A's cursor.
        List<JsonElement> items = await Browser.ListEveryDisputeAsync(limitQuery);

        Assert.Equal(DisputesOfA.Select(dispute => dispute.Id), items.Select(item => item.GetProperty("disputeId").GetGuid()));
    }

    [Fact]
    public async Task ListDisputes_CursorOnAnotherCustomersDispute_ReturnsTheCallersRowsAfterItLikeAnUnheldPosition()
    {
        string onB = DisputesApi.ListCursor(DisputeOfBAtItsOwnInstant.CreatedAt, DisputeOfBAtItsOwnInstant.Id);
        string unheld = DisputesApi.ListCursor(DisputeOfBAtItsOwnInstant.CreatedAt, Guid.CreateVersion7(DisputeOfBAtItsOwnInstant.CreatedAt));

        using JsonDocument afterB = await Browser.ListDisputesPageAsync($"cursor={onB}");
        using JsonDocument afterUnheld = await Browser.ListDisputesPageAsync($"cursor={unheld}");

        List<Guid> expected = [.. DisputesOfA.Where(dispute => dispute.CreatedAt < DisputeOfBAtItsOwnInstant.CreatedAt).Select(dispute => dispute.Id)];
        Assert.Equal(2, expected.Count);
        Assert.Equal(expected, DisputesApi.DisputeIds(afterB));
        Assert.Equal(expected, DisputesApi.DisputeIds(afterUnheld));
    }

    [Fact]
    public async Task ListDisputes_CursorOnAnotherCustomersDisputeAtASharedInstant_ReturnsOnlyTheCallersRowsAfterThatPosition()
    {
        using JsonDocument page = await Browser.ListDisputesPageAsync($"cursor={DisputesApi.ListCursor(DisputeOfBSharingAnInstant.CreatedAt, DisputeOfBSharingAnInstant.Id)}");

        // After B's position in the one newest-first order of every row, keeping only A's.
        List<SeededDispute> everyRow = DisputesApi.NewestFirst(_seeded);
        IEnumerable<Guid> expected = everyRow.Skip(everyRow.IndexOf(DisputeOfBSharingAnInstant) + 1)
            .Where(dispute => dispute.CustomerId == TransactionsApi.CustomerA)
            .Select(dispute => dispute.Id);
        Assert.Equal(expected, DisputesApi.DisputeIds(page));
    }

    [Fact]
    public async Task ListDisputes_CustomerIdInQueryAndHeader_StillReturnsOnlyTheLinkedCustomersDisputes()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"{DisputesApi.RaisePath}?customerId={TransactionsApi.CustomerB}", UriKind.Relative));
        request.Headers.Add("X-Customer-Id", TransactionsApi.CustomerB);

        HttpResponseMessage response = await Browser.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument page = await response.ReadJsonAsync();
        Assert.Equal(DisputesOfA.Select(dispute => dispute.Id), DisputesApi.DisputeIds(page));
    }
}
