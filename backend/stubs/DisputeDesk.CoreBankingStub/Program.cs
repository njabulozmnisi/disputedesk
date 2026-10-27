using DisputeDesk.CoreBankingStub;
using Microsoft.AspNetCore.Mvc;

// A stand-in for the bank's core banking system and the mobile network's SIM-swap check (architecture AD-21).
// It reads fixtures/local.json once at start-up. Customer and account identifiers arrive only in headers or
// request bodies, never in the path or query, so they never appear in a URL or an access log.
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Compose sets an absolute path (/fixtures/local.json); `dotnet run` and the tests use the relative default in
// appsettings.json, which is read from the project directory.
string configuredPath = builder.Configuration["FixturesPath"] ?? throw new InvalidOperationException("FixturesPath is not configured.");
string fixturesPath = Path.IsPathRooted(configuredPath)
    ? configuredPath
    : Path.Combine(builder.Environment.ContentRootPath, configuredPath);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(provider => Bank.Load(fixturesPath, provider.GetRequiredService<TimeProvider>()));

WebApplication app = builder.Build();

// Read the fixtures now, so a missing or broken file stops the stub at start-up instead of on the first call.
app.Services.GetRequiredService<Bank>();

app.MapGet("/health", () => Results.Ok());

app.MapPost("/customers/match", (MatchRequest request, Bank bank) =>
{
    FixtureCustomer? customer = bank.FindByIdNumberAndAccountNumber(request.IdNumber, request.AccountNumber);
    return customer is null
        ? Results.NotFound()
        : Results.Ok(new { customer.CustomerId, customer.Cellphone });
});

app.MapGet("/accounts", ([FromHeader(Name = "X-Customer-Id")] string customerId, Bank bank) =>
{
    FixtureCustomer? customer = bank.FindByCustomerId(customerId);
    return customer is null
        ? Results.NotFound()
        : Results.Ok(customer.Accounts.Select(account => new { account.AccountRef, AccountLast4 = account.AccountNumber[^4..] }));
});

// The cursor is the transaction ID of the last row on the previous page. Over the fixed fixture data this
// gives the same pages as a keyset on (transaction date, transaction ID).
app.MapGet("/transactions", ([FromHeader(Name = "X-Customer-Id")] string customerId, string? cursor, int? limit, Bank bank) =>
{
    IReadOnlyList<TransactionRow>? rows = bank.TransactionsOf(customerId);
    if (rows is null)
    {
        return Results.NotFound();
    }

    int pageSize = limit ?? 20;
    if (pageSize is < 1 or > 50)
    {
        return Results.BadRequest();
    }

    IEnumerable<TransactionRow> remaining = rows;
    if (cursor is not null)
    {
        if (!rows.Any(row => row.TransactionId == cursor))
        {
            return Results.BadRequest();
        }

        remaining = rows.SkipWhile(row => row.TransactionId != cursor).Skip(1);
    }

    // One extra row tells whether there is a next page.
    var items = remaining.Take(pageSize + 1).ToList();
    bool hasNextPage = items.Count > pageSize;
    if (hasNextPage)
    {
        items.RemoveAt(pageSize);
    }

    return Results.Ok(new { items, nextCursor = hasNextPage ? items[^1].TransactionId : null });
});

app.MapGet("/transaction", (
    [FromHeader(Name = "X-Customer-Id")] string customerId,
    [FromHeader(Name = "X-Account-Ref")] string accountRef,
    [FromHeader(Name = "X-Transaction-Id")] string transactionId,
    Bank bank) =>
{
    TransactionRow? row = bank.TransactionsOf(customerId)?
        .FirstOrDefault(row => row.AccountRef == accountRef && row.TransactionId == transactionId);
    return row is null ? Results.NotFound() : Results.Ok(row);
});

app.MapPost("/sim-swap/check", (SimSwapRequest request, Bank bank) =>
{
    FixtureCustomer? customer = bank.FindByCellphone(request.Cellphone);
    return customer is null
        ? Results.NotFound()
        : Results.Ok(new { customer.RecentSimSwap });
});

await app.RunAsync();
