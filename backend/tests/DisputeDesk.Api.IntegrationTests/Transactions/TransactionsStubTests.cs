using DisputeDesk.Api.IntegrationTests.CoreBanking;
using DisputeDesk.Application.Abstractions;
using DisputeDesk.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests.Transactions;

/// <summary>
/// The expectation tests build with <see cref="TransactionsStub.FixtureTransactionDate"/> is the date the real stub gives,
/// at any hour, including 22:00 to 24:00 UTC, when the South African day is already the next UTC date.
/// </summary>
public sealed class TransactionsStubTests
{
    public static TheoryData<DateTimeOffset> ClockReadings =>
    [
        new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 10, 29, 22, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 10, 29, 22, 7, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 10, 29, 23, 59, 59, TimeSpan.Zero).AddTicks(9_999_999),
    ];

    [Theory]
    [MemberData(nameof(ClockReadings))]
    public async Task FixtureTransactionDate_AnyClockReading_IsTheDateTheStubGivesTheTransaction(DateTimeOffset now)
    {
        await using var stub = new TransactionsStub(now);
        await using ServiceProvider services = CoreBankingServices.Build(stub.BaseUrl);
        ICoreBankingClient client = services.GetRequiredService<ICoreBankingClient>();

        // TXN-A-0029 is 2 days ago at 13:24 South African time.
        Result<Transaction> result = await client.GetTransactionAsync(
            TransactionsApi.CustomerA, "ACC-A-1", "TXN-A-0029", TestContext.Current.CancellationToken);

        Assert.Equal(stub.FixtureTransactionDate(daysAgo: 2, new TimeOnly(13, 24)), result.Value.TransactionDate);
    }
}
