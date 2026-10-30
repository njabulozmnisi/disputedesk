using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DisputeDesk.Api.IntegrationTests.CoreBanking;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Infrastructure.Seeding;

namespace DisputeDesk.Api.IntegrationTests.Seeding;

/// <summary>Story 5.4, criterion 7: the seeded disputes in <c>fixtures/local.json</c> are synthetic and stay clear of the tests.</summary>
public sealed partial class SeedFixtureTests
{
    // Named by a backend test or an E2E spec on a fresh stack, which a seeded dispute would make not disputable.
    private static readonly string[] TransactionsTestsName =
        ["TXN-A-0001", "TXN-A-0002", "TXN-A-0024", "TXN-A-0027", "TXN-A-0028", "TXN-A-0029", "TXN-A-0030", "TXN-B-0001", "TXN-B-0026", "TXN-B-0027"];

    [Fact]
    public void Fixtures_SeededNotes_HoldNoCustomerDetailAndNoLongNumber()
    {
        JsonNode fixtures = Read();
        string[] customerDetails =
        [
            .. Array(fixtures, "customers").SelectMany(customer => new[]
            {
                Text(customer, "idNumber"), Text(customer, "cellphone"), Text(customer, "displayName"),
            }),
            .. Array(fixtures, "customers").SelectMany(customer => Array(customer, "accounts")).Select(account => Text(account, "accountNumber")),
        ];
        List<string> notes = Notes(fixtures);

        Assert.NotEmpty(notes);
        Assert.All(notes, note =>
        {
            Assert.All(customerDetails, detail => Assert.DoesNotContain(detail, note, StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotMatch(SixOrMoreDigits(), note);
        });
    }

    [Fact]
    public void Fixtures_SeededTransactions_AreNoneATestOrE2eSpecNames()
    {
        string[] seeded = [.. Array(Read(), "disputes").Select(dispute => Text(dispute, "transactionId"))];

        Assert.NotEmpty(seeded);
        Assert.Empty(seeded.Intersect(TransactionsTestsName));
    }

    [Fact]
    public void Fixtures_RecordsHoldingPrivateData_PrintOnlyTheirTypeOrPosition()
    {
        // Security pitfall 3.8: a record's generated ToString would print every private field if anything logged it.
        var fixtures = Fixtures.Read(FixturesFile.FindPath());
        FixtureDispute dispute = fixtures.Disputes[0];
        List<SeedDispute> planned = SeedDispute.Plan(fixtures, new TestClock().GetUtcNow());

        Assert.Equal(nameof(FixtureCustomer), fixtures.Customers[0].ToString());
        Assert.Equal(nameof(FixtureAccount), fixtures.Customers[0].Accounts[0].ToString());
        Assert.Equal(nameof(FixtureLogin), fixtures.Logins[0].ToString());
        Assert.Equal(nameof(FixtureTransaction), fixtures.Transactions[0].ToString());
        Assert.Equal(nameof(FixtureDispute), dispute.ToString());
        Assert.Equal(nameof(FixtureAction), dispute.Actions[0].ToString());
        Assert.Equal("disputes[0]", planned[0].ToString());
    }

    /// <summary>
    /// Every private value in the fixtures, which no seed message or log line may hold: customer IDs, login subs,
    /// account references and numbers, transaction IDs (those the disputes name included), masked cards and notes.
    /// </summary>
    internal static List<string> PrivateValues(JsonNode fixtures) =>
    [
        .. Array(fixtures, "customers").Select(customer => Text(customer, "customerId")),
        .. Array(fixtures, "customers").SelectMany(customer => Array(customer, "accounts"))
            .SelectMany(account => new[] { Text(account, "accountRef"), Text(account, "accountNumber") }),
        .. Array(fixtures, "logins").Select(login => Text(login, "userId")),
        .. Array(fixtures, "transactions").Select(transaction => Text(transaction, "transactionId")),
        .. Array(fixtures, "transactions").Select(transaction => transaction["maskedCard"]?.GetValue<string>()).OfType<string>(),
        .. Array(fixtures, "disputes").SelectMany(dispute => new[] { Text(dispute, "accountRef"), Text(dispute, "transactionId") }),
        .. Notes(fixtures),
    ];

    /// <summary>Every note the seeded disputes carry: the raise note and each action's note.</summary>
    internal static List<string> Notes(JsonNode fixtures) =>
    [
        .. Array(fixtures, "disputes")
            .SelectMany(dispute => Array(dispute, "actions").Prepend(dispute))
            .Select(node => node["note"]?.GetValue<string>())
            .OfType<string>(),
    ];

    private static JsonNode Read() => JsonNode.Parse(FixturesFile.ReadText()) ?? throw new InvalidOperationException("No fixtures.");

    private static IEnumerable<JsonNode> Array(JsonNode node, string name) =>
        (node[name]?.AsArray() ?? throw new InvalidOperationException($"No {name} array.")).OfType<JsonNode>();

    private static string Text(JsonNode node, string name) => node[name]?.GetValue<string>() ?? throw new InvalidOperationException($"No {name}.");

    [GeneratedRegex("[0-9]{6,}")]
    private static partial Regex SixOrMoreDigits();
}
