using System.Net;
using System.Text.Json.Nodes;
using DisputeDesk.Api.IntegrationTests.CoreBanking;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Infrastructure.Seeding;

namespace DisputeDesk.Api.IntegrationTests.Seeding;

/// <summary>
/// Story 5.4, criterion 7: a fixture dispute the seeder cannot write fails the seed loudly, naming the entry's position
/// and nothing else, so the migrator retries and readiness stays unhealthy; nothing is skipped silently.
/// </summary>
public sealed class InvalidFixtureDisputesTests(PostgresFixture postgres) : IDisposable
{
    private readonly string _fixturesPath = Path.Combine(Path.GetTempPath(), $"disputedesk-fixtures-{Guid.NewGuid():N}.json");

    public static TheoryData<string, string> Breaks => new()
    {
        { "unknown transaction", "disputes[2]" },
        { "another customer's transaction", "disputes[2]" },
        { "instants out of order", "disputes[2]" },
        { "an instant after the seeding instant", "disputes[0]" },
        { "an action the dispute refuses", "disputes[0]" },
        { "a second raise", "disputes[2]" },
        { "a resolve with no outcome", "disputes[4]" },
        { "daysAgo and time on the SLA demo raise", "disputes[6]" },
        { "an outcome on a start review", "disputes[1]" },
        { "a note on a start review", "disputes[1]" },
        { "a note on an escalate", "disputes[2]" },
        { "hoursAfterRaise on a dispute that is not the SLA demo", "disputes[1]" },
        { "hoursAfterRaise beside daysAgo and time", "disputes[6]" },
        { "no SLA demo dispute", "disputes" },
        { "a second SLA demo dispute", "disputes" },
    };

    // The shape faults on a later action: planning finds them, so no fixture dispute is written before the seed fails.
    public static TheoryData<string, string> LaterActionShapeBreaks => new()
    {
        { "a second raise", "disputes[2]" },
        { "a resolve with no outcome", "disputes[4]" },
    };

    [Theory]
    [MemberData(nameof(Breaks))]
    public async Task Seed_InvalidFixtureDispute_ThrowsNamingItsPositionAndReadinessStaysUnhealthy(string fault, string position)
    {
        JsonNode fixtures = await WriteBrokenFixturesAsync(fault);
        // A migrated database: with no migration pending, this migrate and the Api's own take the migration lock in turn.
        await using ApiFactory factory = SeededApi.Create(await postgres.CreateMigratedDatabaseAsync(), new TestClock(), _fixturesPath);
        using HttpClient client = factory.CreateClient();

        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => SeededApi.MigrateAsync(factory));
        HttpResponseMessage ready = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Contains($"Fixture {position}", thrown.Message, StringComparison.Ordinal);
        Assert.All(SeedFixtureTests.PrivateValues(fixtures), value => Assert.DoesNotContain(value, thrown.Message, StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
    }

    [Theory]
    [MemberData(nameof(LaterActionShapeBreaks))]
    public async Task Plan_ShapeFaultOnALaterAction_ThrowsBeforeAnyDisputeIsWritten(string fault, string position)
    {
        await WriteBrokenFixturesAsync(fault);
        var fixtures = Fixtures.Read(_fixturesPath);

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => SeedDispute.Plan(fixtures, new TestClock().GetUtcNow()));

        Assert.Contains($"Fixture {position}", thrown.Message, StringComparison.Ordinal);
    }

    public void Dispose() => File.Delete(_fixturesPath);

    private async Task<JsonNode> WriteBrokenFixturesAsync(string fault)
    {
        JsonNode fixtures = JsonNode.Parse(FixturesFile.ReadText()) ?? throw new InvalidOperationException("No fixtures.");
        JsonNode disputes = fixtures["disputes"] ?? throw new InvalidOperationException("No fixture disputes.");
        Break(disputes, fault);
        await File.WriteAllTextAsync(_fixturesPath, fixtures.ToJsonString(), TestContext.Current.CancellationToken);
        return fixtures;
    }

    private static void Break(JsonNode disputes, string fault)
    {
        switch (fault)
        {
            case "unknown transaction":
                disputes[2]!["transactionId"] = "TXN-A-9999";
                break;
            case "another customer's transaction":
                // Customer B's real transaction, on B's account, on a dispute of customer A.
                disputes[2]!["accountRef"] = "ACC-B-1";
                disputes[2]!["transactionId"] = "TXN-B-0020";
                break;
            case "instants out of order":
                // Escalated 30 days ago, before the review it follows started 24 days ago.
                disputes[2]!["actions"]![2]!["daysAgo"] = 30;
                break;
            case "an instant after the seeding instant":
                disputes[0]!["actions"]![0]!["daysAgo"] = -1;
                break;
            case "an action the dispute refuses":
                // Resolve straight from Submitted: the allowed-actions table refuses it (FR-24).
                disputes[0]!["actions"]!.AsArray().Add(JsonNode.Parse("""{"action": "resolve", "daysAgo": 0, "time": "00:00:00", "outcome": "upheld", "note": "Done."}"""));
                break;
            case "a second raise":
                // After the escalate 15 days ago, so only the action type is wrong.
                disputes[2]!["actions"]!.AsArray().Add(JsonNode.Parse("""{"action": "raise", "daysAgo": 10, "time": "09:00:00"}"""));
                break;
            case "a resolve with no outcome":
                disputes[4]!["actions"]![2]!.AsObject().Remove("outcome");
                break;
            case "daysAgo and time on the SLA demo raise":
                disputes[6]!["actions"]![0]!["daysAgo"] = 12;
                disputes[6]!["actions"]![0]!["time"] = "09:00:00";
                break;
            case "an outcome on a start review":
                disputes[1]!["actions"]![1]!["outcome"] = "upheld";
                break;
            case "a note on a start review":
                disputes[1]!["actions"]![1]!["note"] = "Looking into it.";
                break;
            case "a note on an escalate":
                disputes[2]!["actions"]![2]!["note"] = "Sent up the line.";
                break;
            case "hoursAfterRaise on a dispute that is not the SLA demo":
                // Two days ago at 08:30 plus 2 hours: still in order and before the seeding instant.
                disputes[1]!["actions"]![1]!.AsObject().Remove("daysAgo");
                disputes[1]!["actions"]![1]!.AsObject().Remove("time");
                disputes[1]!["actions"]![1]!["hoursAfterRaise"] = 2;
                break;
            case "hoursAfterRaise beside daysAgo and time":
                disputes[6]!["actions"]![1]!["daysAgo"] = 1;
                disputes[6]!["actions"]![1]!["time"] = "09:00:00";
                break;
            case "no SLA demo dispute":
                disputes[6]!["slaDemo"] = false;
                break;
            case "a second SLA demo dispute":
                disputes[1]!["slaDemo"] = true;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(fault), fault, "No such break.");
        }
    }
}
