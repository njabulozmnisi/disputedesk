using System.Globalization;
using System.Net;
using System.Text.Json;
using DisputeDesk.Api.IntegrationTests.CoreBanking;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using Microsoft.AspNetCore.Http;

namespace DisputeDesk.Api.IntegrationTests.Disputes;

/// <summary>
/// AC8: the snapshot's transaction date is stored in UTC at the whole microseconds a Postgres <c>timestamptz</c> keeps
/// (PostgreSQL, Date/Time Types; AD-4), so the raise 201, the detail and the history list show the same instant in the same
/// string.
/// </summary>
public class SnapshotPrecisionTests(PostgresFixture postgres)
{
    [Fact]
    public async Task GetDispute_TransactionDateWithSevenFractionalDigitsAndAnOffset_ShowsTheSameStringAsTheRaise()
    {
        DateTimeOffset now = TodayAtAWholeSecond();
        await using FakeCoreBankingServer coreBanking = await CoreBankingWithASevenDigitDateAsync(now);
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, coreBanking.BaseUrl, now);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        JsonElement created = await browser.RaiseCreatedAsync(DisputesApi.Body());

        HttpResponseMessage response = await browser.GetDisputeAsync(created.GetProperty("disputeId").GetString() ?? "");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = await response.ReadJsonAsync();
        string? createdDate = created.GetProperty("transaction").GetProperty("transactionDate").GetString();
        Assert.Equal(createdDate, body.RootElement.GetProperty("transaction").GetProperty("transactionDate").GetString());
        Assert.EndsWith(".123456+00:00", createdDate, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListDisputes_CoreBankingDownAfterTheRaise_Returns200WithTheSnapshotDateAsTheRaiseAndDetailShowIt()
    {
        // Story 3.9 AC6 (AD-21): the list reads only the stored snapshot, so it answers with core banking stopped and
        // never calls it.
        DateTimeOffset now = TodayAtAWholeSecond();
        FakeCoreBankingServer coreBanking = await CoreBankingWithASevenDigitDateAsync(now);
        try
        {
            await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, coreBanking.BaseUrl, now);
            using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
            JsonElement created = await browser.RaiseCreatedAsync(DisputesApi.Body());
            // The outage: core banking stops here. The finally below stops it again if anything before this line fails.
            await coreBanking.DisposeAsync();

            HttpResponseMessage response = await browser.ListDisputesAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using JsonDocument page = await response.ReadJsonAsync();
            using JsonDocument detail = await (await browser.GetDisputeAsync(created.GetProperty("disputeId").GetString() ?? "")).ReadJsonAsync();
            string? createdDate = created.GetProperty("transaction").GetProperty("transactionDate").GetString();
            JsonElement item = Assert.Single(page.RootElement.GetProperty("items").EnumerateArray());
            Assert.Equal(createdDate, item.GetProperty("transaction").GetProperty("transactionDate").GetString());
            Assert.Equal(createdDate, detail.RootElement.GetProperty("transaction").GetProperty("transactionDate").GetString());
            Assert.EndsWith(".123456+00:00", createdDate, StringComparison.Ordinal);
        }
        finally
        {
            await coreBanking.DisposeAsync();
        }
    }

    // The Api's clock is today, fixed, because the sign-in cookies expire 15 minutes after it.
    private static DateTimeOffset TodayAtAWholeSecond() => DateTimeOffset.FromUnixTimeSeconds(TimeProvider.System.GetUtcNow().ToUnixTimeSeconds());

    /// <summary>
    /// Core banking answering every request with customer A's TXN-A-0029, dated two days before <paramref name="now"/>
    /// in South African time and a tick finer than Postgres keeps: .1234567 s.
    /// </summary>
    private static Task<FakeCoreBankingServer> CoreBankingWithASevenDigitDateAsync(DateTimeOffset now)
    {
        DateTimeOffset transactionDate = now.AddDays(-2).ToOffset(TimeSpan.FromHours(2)).AddTicks(1_234_567);
        string transaction = $$"""
            {"transactionId":"TXN-A-0029","accountRef":"ACC-A-1","accountLast4":"0011",
             "transactionDate":"{{transactionDate.ToString("O", CultureInfo.InvariantCulture)}}","description":"Steers Braamfontein",
             "amount":"87.23","currency":"ZAR","direction":"debit","kind":"card","state":"settled","maskedCard":"492181******4417"}
            """;
        return FakeCoreBankingServer.StartAsync(context =>
        {
            context.Response.ContentType = "application/json";
            return context.Response.WriteAsync(transaction, context.RequestAborted);
        });
    }
}
