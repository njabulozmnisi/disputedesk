namespace DisputeDesk.Infrastructure.Persistence;

/// <summary>Where an outbox row is in the relay (AD-17).</summary>
public enum OutboxStatus
{
    Pending,
    Sent,

    /// <summary>The broker refused it 5 times; later events of its dispute wait for an operator.</summary>
    Failed,
}
