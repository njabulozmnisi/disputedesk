namespace DisputeDesk.Worker.IntegrationTests.Outbox;

/// <summary>
/// The relay tests run one at a time, after the parallel tests: the advisory-lock key is fixed, the database is shared
/// and the heartbeat file is one path (xUnit, Running tests in parallel).
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class OutboxRelayTestGroup : ICollectionFixture<OutboxFixture>
{
    public const string Name = "Outbox relay";
}
