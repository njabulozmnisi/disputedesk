using System.Net;
using System.Text.Json;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Contracts.Problems;

namespace DisputeDesk.Api.IntegrationTests.Disputes.Idempotency;

/// <summary>
/// AC1: a raise without a well-formed <c>Idempotency-Key</c> gets 400 <c>validation</c> before any insert; a quoted key
/// is accepted; <c>openapi.json</c> declares the header as required. The form itself is unit-tested in
/// <see cref="IdempotencyKeyHeaderTests"/>.
/// </summary>
public class IdempotencyKeyRequiredTests(PostgresFixture postgres, TransactionsStub stub) : IClassFixture<TransactionsStub>
{
    public static TheoryData<string> MalformedKeys => new()
    {
        "abc",                                    // unquoted
        "8e03978e-40d5-43e8-bc93-6894a57f9324",
        "\"\"",                                   // empty
        $"\"{new string('k', 256)}\"",            // oversized
        $"\"{new string('k', 10_000)}\"",
        "\"a\";p=1",                              // a parameter
    };

    [Theory]
    [MemberData(nameof(MalformedKeys))]
    public async Task Raise_MalformedKey_Returns400ValidationAndWritesNothing(string key)
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);

        HttpResponseMessage response = await browser.RaiseWithKeyAsync(DisputesApi.Body(), key);

        await AssertRefusedBeforeAnyInsertAsync(factory, response);
    }

    [Fact]
    public async Task Raise_NoKey_Returns400ValidationAndWritesNothing()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);

        HttpResponseMessage response = await browser.RaiseWithKeyAsync(DisputesApi.Body(), idempotencyKey: null);

        await AssertRefusedBeforeAnyInsertAsync(factory, response);
    }

    [Fact]
    public async Task Raise_KeyHeaderSentTwice_Returns400ValidationAndWritesNothing()
    {
        // Two header lines combine into a list (RFC 9651 §4.2), which is not one String, even when both are the same.
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        string key = DisputesApi.NewKey();

        HttpResponseMessage response = await browser.RaiseAsync(DisputesApi.Body(), await browser.AntiforgeryTokenAsync(), key, key);

        await AssertRefusedBeforeAnyInsertAsync(factory, response);
    }

    private static async Task AssertRefusedBeforeAnyInsertAsync(ApiFactory factory, HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemType.For(CommonProblems.Validation), await response.ProblemTypeAsync());
        Assert.Empty(await factory.KeyRowsAsync());
        Assert.Equal(RowCounts.None, await factory.CountRowsAsync());
    }

    [Theory]
    [InlineData("\"8e03978e-40d5-43e8-bc93-6894a57f9324\"")]
    [InlineData("\"a\"")]
    [InlineData("\"with \\\"quotes\\\" and spaces\"")]
    public async Task Raise_QuotedKey_Returns201AndStoresTheDecodedKey(string header)
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);

        HttpResponseMessage response = await browser.RaiseWithKeyAsync(DisputesApi.Body(), header);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        string decoded = header[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal);
        Assert.Equal(decoded, Assert.Single(await factory.KeyRowsAsync()).Key);
    }

    [Fact]
    public async Task Raise_KeyOf255Characters_Returns201()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);

        HttpResponseMessage response = await browser.RaiseWithKeyAsync(DisputesApi.Body(), $"\"{new string('k', 255)}\"");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Raise_BodyBreakingAFieldRule_Returns400BeforeTheKeyIsClaimed()
    {
        // Built-in validation runs before the idempotency filter: the 400 is not stored, and the key stays free.
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        string key = DisputesApi.NewKey();

        HttpResponseMessage broken = await browser.RaiseWithKeyAsync(DisputesApi.Body(reason: "wrongAmount"), key);

        await broken.AssertValidationProblemAsync("expectedAmount");
        Assert.Empty(await factory.KeyRowsAsync());
        Assert.Equal(HttpStatusCode.Created, (await browser.RaiseWithKeyAsync(DisputesApi.Body(), key)).StatusCode);
    }

    [Fact]
    public async Task Document_RaiseOperation_DeclaresIdempotencyKeyAsARequiredHeader()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString, environment: "Development");
        using HttpClient client = factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken);
        using JsonDocument document = await response.ReadJsonAsync();

        JsonElement parameter = Assert.Single(
            document.RootElement.GetProperty("paths").GetProperty("/api/v1/disputes").GetProperty("post").GetProperty("parameters").EnumerateArray());
        Assert.Equal("Idempotency-Key", parameter.GetProperty("name").GetString());
        Assert.Equal("header", parameter.GetProperty("in").GetString());
        Assert.True(parameter.GetProperty("required").GetBoolean());
    }
}
