using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Contracts.Json;

namespace DisputeDesk.Api.IntegrationTests.Disputes;

/// <summary>
/// Story 3.9 AC7 (NFR-API-1, NFR-API-6): every response timestamp on the customer routes is RFC 3339 UTC with the
/// offset <c>+00:00</c>, written in System.Text.Json's default <c>DateTimeOffset</c> format, with no custom converter.
/// </summary>
public sealed partial class WireFormatTests(PostgresFixture postgres, TransactionsStub stub) : IClassFixture<TransactionsStub>
{
    [Fact]
    public async Task CustomerRoutes_EveryResponseTimestamp_IsRfc3339UtcAsSystemTextJsonWritesIt()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        JsonElement created = await browser.RaiseCreatedAsync(DisputesApi.Body());
        using JsonDocument detail = await (await browser.GetDisputeAsync(created.GetProperty("disputeId").GetString() ?? "")).ReadJsonAsync();
        using JsonDocument list = await browser.ListDisputesPageAsync();
        using JsonDocument transactions = await (await browser.GetPathAsync("/api/v1/transactions?limit=50")).ReadJsonAsync();

        List<string> timestamps =
        [
            .. transactions.RootElement.GetProperty("items").EnumerateArray().Select(row => Text(row, "transactionDate")),
            .. DisputeTimestamps(created),
            .. DisputeTimestamps(detail.RootElement),
            .. list.RootElement.GetProperty("items").EnumerateArray()
                .SelectMany(item => new[] { Text(item, "createdAt"), Text(item.GetProperty("transaction"), "transactionDate") }),
        ];

        // Customer A's 48 fixture transactions; the raise and the detail each with createdAt, transactionDate and one
        // timeline entry; the one list item with createdAt and transactionDate.
        Assert.Equal(48 + 3 + 3 + 2, timestamps.Count);
        Assert.All(timestamps, timestamp =>
        {
            Assert.Matches(Rfc3339Utc(), timestamp);
            // Exactly what the shared options write for the instant it names: the default format, no custom converter.
            var instant = DateTimeOffset.Parse(timestamp, CultureInfo.InvariantCulture);
            Assert.Equal($"\"{timestamp}\"", JsonSerializer.Serialize(instant, DisputeDeskJson.Options));
        });
    }

    /// <summary><c>createdAt</c>, <c>transaction.transactionDate</c> and each <c>timeline[].occurredAt</c> of a raise 201 or detail.</summary>
    private static IEnumerable<string> DisputeTimestamps(JsonElement dispute) =>
    [
        Text(dispute, "createdAt"),
        Text(dispute.GetProperty("transaction"), "transactionDate"),
        .. dispute.GetProperty("timeline").EnumerateArray().Select(entry => Text(entry, "occurredAt")),
    ];

    private static string Text(JsonElement element, string property) => element.GetProperty(property).GetString() ?? "";

    // RFC 3339 date-time with fractional seconds when there are any, and the UTC offset written as +00:00.
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?\+00:00$")]
    private static partial Regex Rfc3339Utc();
}
