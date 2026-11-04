using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Application.Disputes;
using DisputeDesk.Domain.Disputes;

namespace DisputeDesk.Api.IntegrationTests.Disputes;

/// <summary>The test helper that takes a dispute to a status gives each step of the way its own instant.</summary>
public class DisputeInTests(PostgresFixture postgres)
{
    [Fact]
    public async Task DisputeIn_Resolved_StoresEachStepAtItsOwnInstantOneSecondApart()
    {
        await using ApiFactory factory = await DisputesApi.StartWithoutCoreBankingAsync(postgres);
        DateTimeOffset raisedAt = DisputesApi.SeedInstant;
        DateTimeOffset actedAt = raisedAt.AddHours(1);

        Guid disputeId = (await factory.DisputeInAsync(DisputeStatus.Resolved, raisedAt: raisedAt, actedAt: actedAt)).Id;

        List<StoredEventRow> stream = await factory.StreamAsync(disputeId);
        Assert.Equal([nameof(DisputeRaised), nameof(ReviewStarted), nameof(DisputeResolved)], stream.Select(row => row.EventType));
        Assert.Equal([raisedAt, actedAt, actedAt.AddSeconds(1)], stream.Select(row => row.OccurredAtUtc));
    }
}
