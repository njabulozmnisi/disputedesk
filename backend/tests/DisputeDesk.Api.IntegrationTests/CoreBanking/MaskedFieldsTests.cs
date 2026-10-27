using DisputeDesk.Application.Abstractions;
using DisputeDesk.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests.CoreBanking;

/// <summary>
/// Masking is checked where core banking's answer enters DisputeDesk: a card that is not first 6 and last 4, or an
/// account that is not just its last 4 digits, makes the answer Unavailable, so it is never passed on.
/// </summary>
public sealed class MaskedFieldsTests
{
    private const string CustomerA = "CUS-000001";
    private const string MaskedCard = "492181******4417";
    private const string FullCardNumber = "4921810000004417";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public static TheoryData<string, string?> UnmaskedFields => new()
    {
        { "0011", FullCardNumber },
        { "1000000011", MaskedCard },   // the full account number
        { "001", MaskedCard },
        { "0011", "492181*****4417" },
    };

    [Theory]
    [MemberData(nameof(UnmaskedFields))]
    public async Task GetTransactionsAsync_RowNotMasked_ReturnsUnavailable(string accountLast4, string? maskedCard)
    {
        await using FakeCoreBankingServer stub = await AnswerAsync($$"""{"items":[{{Row(accountLast4, maskedCard)}}],"nextCursor":null}""");
        await using ServiceProvider services = CoreBankingServices.Build(stub.BaseUrl);
        ICoreBankingClient client = services.GetRequiredService<ICoreBankingClient>();

        Result<TransactionPage> result = await client.GetTransactionsAsync(CustomerA, cursor: null, limit: 20, CancellationToken);

        Assert.Equal(CoreBankingErrors.Unavailable, result.Error);
    }

    [Fact]
    public async Task GetTransactionsAsync_RowNotMasked_LogsAValueFreeWarning()
    {
        await using FakeCoreBankingServer stub = await AnswerAsync($$"""{"items":[{{Row("0011", FullCardNumber)}}],"nextCursor":null}""");
        var logs = new LogCapture();
        await using ServiceProvider services = CoreBankingServices.Build(stub.BaseUrl, logs);
        ICoreBankingClient client = services.GetRequiredService<ICoreBankingClient>();

        await client.GetTransactionsAsync(CustomerA, cursor: null, limit: 20, CancellationToken);

        Assert.Contains("Core banking answered with an unmasked account or card number; treated as unavailable", logs.Lines);
        Assert.DoesNotContain(logs.Lines, line => line.Contains(FullCardNumber, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("""{"items":[{"transactionId":"TXN-A-0001","accountRef":"ACC-A-1","transactionDate":"2026-10-20T10:00:00+00:00","description":"Shop","amount":12.50,"currency":"ZAR","direction":"debit","kind":"card","state":"settled","maskedCard":null}],"nextCursor":null}""")]
    [InlineData("""{"items":[{"transactionId":"TXN-A-0001","accountRef":"ACC-A-1","accountLast4":null,"transactionDate":"2026-10-20T10:00:00+00:00","description":"Shop","amount":12.50,"currency":"ZAR","direction":"debit","kind":"card","state":"settled","maskedCard":null}],"nextCursor":null}""")]
    [InlineData("""{"nextCursor":null}""")]
    public async Task GetTransactionsAsync_RequiredFieldMissingOrNull_ReturnsUnavailable(string json)
    {
        await using FakeCoreBankingServer stub = await AnswerAsync(json);
        await using ServiceProvider services = CoreBankingServices.Build(stub.BaseUrl);
        ICoreBankingClient client = services.GetRequiredService<ICoreBankingClient>();

        Result<TransactionPage> result = await client.GetTransactionsAsync(CustomerA, cursor: null, limit: 20, CancellationToken);

        Assert.Equal(CoreBankingErrors.Unavailable, result.Error);
    }

    [Fact]
    public async Task GetTransactionsAsync_RowsMaskedOrWithoutACard_ReturnsThem()
    {
        await using FakeCoreBankingServer stub = await AnswerAsync($$"""{"items":[{{Row("0011", MaskedCard)}},{{Row("0011", null)}}],"nextCursor":null}""");
        await using ServiceProvider services = CoreBankingServices.Build(stub.BaseUrl);
        ICoreBankingClient client = services.GetRequiredService<ICoreBankingClient>();

        Result<TransactionPage> result = await client.GetTransactionsAsync(CustomerA, cursor: null, limit: 20, CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Items.Count);
    }

    [Fact]
    public async Task GetTransactionAsync_FullCardNumber_ReturnsUnavailable()
    {
        await using FakeCoreBankingServer stub = await AnswerAsync(Row("0011", FullCardNumber));
        await using ServiceProvider services = CoreBankingServices.Build(stub.BaseUrl);
        ICoreBankingClient client = services.GetRequiredService<ICoreBankingClient>();

        Result<Transaction> result = await client.GetTransactionAsync(CustomerA, "ACC-A-1", "TXN-A-0001", CancellationToken);

        Assert.Equal(CoreBankingErrors.Unavailable, result.Error);
    }

    [Fact]
    public async Task GetAccountsAsync_FullAccountNumber_ReturnsUnavailable()
    {
        await using FakeCoreBankingServer stub = await AnswerAsync("""[{"accountRef":"ACC-A-1","accountLast4":"1000000011"}]""");
        await using ServiceProvider services = CoreBankingServices.Build(stub.BaseUrl);
        ICoreBankingClient client = services.GetRequiredService<ICoreBankingClient>();

        Result<IReadOnlyList<Account>> result = await client.GetAccountsAsync(CustomerA, CancellationToken);

        Assert.Equal(CoreBankingErrors.Unavailable, result.Error);
    }

    private static string Row(string accountLast4, string? maskedCard)
    {
        string card = maskedCard is null ? "null" : $"\"{maskedCard}\"";
        return $$"""
            {"transactionId":"TXN-A-0001","accountRef":"ACC-A-1","accountLast4":"{{accountLast4}}","transactionDate":"2026-10-20T10:00:00+00:00",
             "description":"Shop","amount":12.50,"currency":"ZAR","direction":"debit","kind":"card","state":"settled","maskedCard":{{card}}}
            """;
    }

    private static Task<FakeCoreBankingServer> AnswerAsync(string json) => FakeCoreBankingServer.StartAsync(context =>
    {
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync(json, context.RequestAborted);
    });
}
