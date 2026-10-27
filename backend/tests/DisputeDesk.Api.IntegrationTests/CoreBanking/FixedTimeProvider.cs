namespace DisputeDesk.Api.IntegrationTests.CoreBanking;

/// <summary>A clock that always reads the same instant.</summary>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
