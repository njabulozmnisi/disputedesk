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
/// (PostgreSQL, Date/Time Types; AD-4), so the raise 201 and the detail show the same instant in the same string.
/// </summary>
public class SnapshotPrecisionTests(PostgresFixture postgres)
{
    [Fact]
    public async Task GetDispute_TransactionDateWithSevenFractionalDigitsAndAnOffset_ShowsTheSameStringAsTheRaise()
    {
        // The Api's clock is today, fixed, because the sign-in cookies expire 15 minutes after it.
        var now = DateTimeOffset.FromUnixTimeSeconds(TimeProvider.System.GetUtcNow().ToUnixTimeSeconds());
        // Two days ago in South African time, a tick finer than Postgres keeps: .1234567 s.
        DateTimeOffset transactionDate = now.AddDays(-2).ToOffset(TimeSpan.FromHours(2)).AddTicks(1_234_567);
        string transaction = $$"""
            {"transactionId":"TXN-A-0029","accountRef":"ACC-A-1","accountLast4":"0011",
             "transactionDate":"{{transactionDate.ToString("O", CultureInfo.InvariantCulture)}}","description":"Steers Braamfontein",
             "amount":"87.23","currency":"ZAR","direction":"debit","kind":"card","state":"settled","maskedCard":"492181******4417"}
            """;
        await using FakeCoreBankingServer coreBanking = await FakeCoreBankingServer.StartAsync(context =>
        {
            context.Response.ContentType = "application/json";
            return context.Response.WriteAsync(transaction, context.RequestAborted);
        });
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
}
