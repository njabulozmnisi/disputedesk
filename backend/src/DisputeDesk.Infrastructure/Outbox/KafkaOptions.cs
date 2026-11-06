namespace DisputeDesk.Infrastructure.Outbox;

/// <summary>The <c>Kafka</c> configuration section (AD-26). It holds an address only; no credential.</summary>
public sealed class KafkaOptions
{
    public const string SectionName = "Kafka";

    /// <summary>The broker: <c>localhost:19092</c> for a host run outside Compose, <c>redpanda:9092</c> in Compose.</summary>
    public string BootstrapServers { get; set; } = "";
}
