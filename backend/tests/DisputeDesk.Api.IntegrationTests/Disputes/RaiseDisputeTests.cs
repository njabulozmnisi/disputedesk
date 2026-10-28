using System.Net;
using System.Text.Json;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests.Disputes;

/// <summary>AC1 and AC4: a raise that succeeds, its response, its snapshot and the wrong-amount difference.</summary>
public class RaiseDisputeTests(PostgresFixture postgres, TransactionsStub stub) : IClassFixture<TransactionsStub>
{
    [Fact]
    public async Task Raise_DisputableTransaction_Returns201SubmittedWithOneRaiseEntryByYouAndItsLocation()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);

        HttpResponseMessage response = await browser.RaiseAsync(DisputesApi.Body(note: "  I never bought this  "));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using JsonDocument body = await response.ReadJsonAsync();
        JsonElement dispute = body.RootElement;
        string disputeId = dispute.GetProperty("disputeId").GetString() ?? "";
        Assert.Equal(new Uri($"/api/v1/disputes/{disputeId}", UriKind.Relative), response.Headers.Location);
        Assert.Equal("submitted", dispute.GetProperty("customerStatus").GetString());
        Assert.Equal("unrecognised", dispute.GetProperty("reason").GetString());
        Assert.Equal("I never bought this", dispute.GetProperty("note").GetString());
        JsonElement entry = Assert.Single(dispute.GetProperty("timeline").EnumerateArray());
        Assert.Equal("raise", entry.GetProperty("action").GetString());
        Assert.Equal("you", entry.GetProperty("actor").GetString());
        Assert.Equal("submitted", entry.GetProperty("customerStatus").GetString());
        Assert.Equal("I never bought this", entry.GetProperty("note").GetString());
        Assert.Equal(RowCounts.One, await factory.CountRowsAsync());
    }

    [Fact]
    public async Task Raise_DisputableTransaction_ResponseCarriesNoActorIdentitySlaOrCustomerId()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);

        HttpResponseMessage response = await browser.RaiseAsync(DisputesApi.Body());

        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("login-a", body, StringComparison.Ordinal);
        Assert.DoesNotContain(TransactionsApi.CustomerA, body, StringComparison.Ordinal);
        Assert.DoesNotContain("sla", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("actorType", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Raise_AmountDateAndDescriptionInTheBody_AreIgnoredForTheCoreBankingSnapshot()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        string json = """
            {"accountRef":"ACC-A-1","transactionId":"TXN-A-0029","reason":"unrecognised",
             "amount":"1.00","transactionDate":"2020-01-01T00:00:00Z","description":"Forged","currency":"USD",
             "disputedAmount":"999.99","customerId":"CUS-000002","status":"resolved"}
            """;

        HttpResponseMessage response = await browser.RaiseAsync(json);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using JsonDocument body = await response.ReadJsonAsync();
        JsonElement transaction = body.RootElement.GetProperty("transaction");
        Assert.Equal("""{"amount":"87.23","currency":"ZAR"}""", transaction.GetProperty("amount").GetRawText());
        Assert.Equal("Steers Braamfontein", transaction.GetProperty("description").GetString());
        Assert.Equal("0011", transaction.GetProperty("accountLast4").GetString());
        Assert.Equal("492181******4417", transaction.GetProperty("maskedCard").GetString());
        Assert.Equal(stub.Now.AddDays(-2).UtcDateTime.Date, transaction.GetProperty("transactionDate").GetDateTimeOffset().UtcDateTime.Date);
        Assert.Equal("""{"amount":"87.23","currency":"ZAR"}""", body.RootElement.GetProperty("disputedAmount").GetRawText());
        Assert.Equal("submitted", body.RootElement.GetProperty("customerStatus").GetString());

        Dispute stored = await SingleDisputeAsync(factory);
        Assert.Equal(TransactionsApi.CustomerA, stored.CustomerId);
        Assert.Equal(87.23m, stored.TransactionAmount);
        Assert.Equal("ZAR", stored.Currency);
        Assert.Equal("Steers Braamfontein", stored.Description);
    }

    [Fact]
    public async Task Raise_DisputableTransaction_StampsTheRowsWithTheOneUseCaseInstant()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);

        await browser.RaiseAsync(DisputesApi.Body());

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Dispute dispute = await db.Set<Dispute>().IgnoreQueryFilters().SingleAsync(TestContext.Current.CancellationToken);
        TimelineEntry entry = await db.Set<TimelineEntry>().IgnoreQueryFilters().SingleAsync(TestContext.Current.CancellationToken);
        Application.Audit.AuditLogEntry audit = await db.Set<Application.Audit.AuditLogEntry>().SingleAsync(TestContext.Current.CancellationToken);
        OutboxMessage outbox = await db.Set<OutboxMessage>().SingleAsync(TestContext.Current.CancellationToken);
        // The Api's clock is fixed at the stub's instant; Postgres keeps microseconds, so compare at that precision.
        DateTimeOffset now = stub.Now.AddTicks(-(stub.Now.Ticks % 10));
        Assert.Equal(now, dispute.CreatedAtUtc);
        Assert.Equal(now.AddHours(240), dispute.SlaDueAtUtc);
        Assert.Equal(now, entry.OccurredAtUtc);
        Assert.Equal(now, audit.OccurredAtUtc);
        Assert.Equal(now, outbox.CreatedAtUtc);
    }

    [Theory]
    [InlineData("80.00", "7.23")]
    [InlineData("0", "87.23")]
    [InlineData("0.00", "87.23")]
    [InlineData("87.22", "0.01")]
    [InlineData("87.2", "0.03")]
    public async Task Raise_WrongAmountBelowTheTransactionAmount_DisputesTheDifference(string expected, string disputed)
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);

        HttpResponseMessage response = await browser.RaiseAsync(DisputesApi.Body(reason: "wrongAmount", expectedAmount: expected));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using JsonDocument body = await response.ReadJsonAsync();
        Assert.Equal($$"""{"amount":"{{disputed}}","currency":"ZAR"}""", body.RootElement.GetProperty("disputedAmount").GetRawText());
        string expectedTwoDecimals = decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal($$"""{"amount":"{{expectedTwoDecimals}}","currency":"ZAR"}""", body.RootElement.GetProperty("expectedAmount").GetRawText());
    }

    [Fact]
    public async Task Raise_NoteOf1000CharactersAfterTrimming_IsAccepted()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);

        HttpResponseMessage response = await browser.RaiseAsync(DisputesApi.Body(note: "   " + new string('a', 1000) + "   "));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1000, (await SingleDisputeAsync(factory)).Note?.Length);
    }

    [Fact]
    public async Task Raise_WhitespaceNote_StoresNoNote()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);

        HttpResponseMessage response = await browser.RaiseAsync(DisputesApi.Body(note: " \n\t "));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Null((await SingleDisputeAsync(factory)).Note);
    }

    private static async Task<Dispute> SingleDisputeAsync(ApiFactory factory)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<Dispute>().IgnoreQueryFilters()
            .SingleAsync(TestContext.Current.CancellationToken);
    }
}
