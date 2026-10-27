using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DisputeDesk.Api.IntegrationTests.CoreBanking;

/// <summary>The masked transaction shape the stub returns, read as raw JSON (AC3).</summary>
public sealed partial class TransactionShapeTests(CoreBankingStubFactory stub) : IClassFixture<CoreBankingStubFactory>
{
    private static readonly string[] CustomerIds = ["CUS-000001", "CUS-000002", "CUS-000003", "CUS-000004"];

    private static readonly string[] RowFields =
        ["accountLast4", "accountRef", "amount", "currency", "description", "direction", "kind", "maskedCard", "state", "transactionDate", "transactionId"];

    [Fact]
    public async Task Transactions_EveryFixtureRow_HasExactlyTheMaskedFields()
    {
        foreach (string customerId in CustomerIds)
        {
            using var page = JsonDocument.Parse(await GetAsync("transactions?limit=50", customerId));

            foreach (JsonElement row in page.RootElement.GetProperty("items").EnumerateArray())
            {
                Assert.Equal(RowFields, row.EnumerateObject().Select(field => field.Name).Order(StringComparer.Ordinal));
                Assert.Matches(Last4(), row.GetProperty("accountLast4").GetString());

                JsonElement maskedCard = row.GetProperty("maskedCard");
                if (maskedCard.ValueKind != JsonValueKind.Null)
                {
                    Assert.Matches(First6AndLast4(), maskedCard.GetString());
                }
            }
        }
    }

    [Fact]
    public async Task TransactionFixturesAndStubResponses_AnyText_HoldNoFullAccountNumber()
    {
        using JsonDocument fixtures = FixturesFile.Read();
        string[] accountNumbers = [.. fixtures.RootElement.GetProperty("customers").EnumerateArray()
            .SelectMany(customer => customer.GetProperty("accounts").EnumerateArray())
            .Select(account => account.GetProperty("accountNumber").GetString() ?? string.Empty)];

        // Only the bank records in customers[].accounts[] keep the full account number, for matching.
        List<string> texts = [fixtures.RootElement.GetProperty("transactions").GetRawText(), .. await AllStubResponsesAsync(fixtures)];

        Assert.All(texts, text =>
            Assert.All(accountNumbers, accountNumber => Assert.DoesNotContain(accountNumber, text, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task FixtureFileAndStubResponses_AnyText_HoldNoFullCardNumber()
    {
        using JsonDocument fixtures = FixturesFile.Read();

        // ID numbers are 13 digits by design, so take them out first; then no run of 13 or more digits may remain.
        string fixtureFileWithoutIdNumbers = IdNumberField().Replace(FixturesFile.ReadText(), string.Empty);
        List<string> texts = [fixtureFileWithoutIdNumbers, .. await AllStubResponsesAsync(fixtures)];

        Assert.All(texts, text => Assert.DoesNotMatch(CardNumberLengthDigits(), text));
    }

    /// <summary>Every kind of stub response, for every fixture customer.</summary>
    private async Task<List<string>> AllStubResponsesAsync(JsonDocument fixtures)
    {
        var responses = new List<string>();
        foreach (JsonElement customer in fixtures.RootElement.GetProperty("customers").EnumerateArray())
        {
            string customerId = customer.GetProperty("customerId").GetString() ?? string.Empty;
            string? idNumber = customer.GetProperty("idNumber").GetString();
            string? accountNumber = customer.GetProperty("accounts")[0].GetProperty("accountNumber").GetString();
            string? cellphone = customer.GetProperty("cellphone").GetString();

            responses.Add(await GetAsync("transactions?limit=50", customerId));
            responses.Add(await GetAsync("accounts", customerId));
            responses.Add(await PostAsync("customers/match", new { idNumber, accountNumber }));
            responses.Add(await PostAsync("sim-swap/check", new { cellphone }));
        }

        return responses;
    }

    private async Task<string> PostAsync(string path, object body)
    {
        using HttpClient client = stub.CreateClient();
        using HttpResponseMessage response = await client.PostAsJsonAsync(new Uri(stub.BaseUrl, path), body, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    private async Task<string> GetAsync(string path, string customerId)
    {
        using HttpClient client = stub.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(stub.BaseUrl, path));
        request.Headers.Add("X-Customer-Id", customerId);
        using HttpResponseMessage response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    [GeneratedRegex(@"^\d{4}$")]
    private static partial Regex Last4();

    [GeneratedRegex(@"^\d{6}\*{6}\d{4}$")]
    private static partial Regex First6AndLast4();

    [GeneratedRegex(@"""idNumber"":\s*""\d+""")]
    private static partial Regex IdNumberField();

    // A card number has 13 to 19 digits.
    [GeneratedRegex(@"\d{13,}")]
    private static partial Regex CardNumberLengthDigits();
}
