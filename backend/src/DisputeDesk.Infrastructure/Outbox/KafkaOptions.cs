using System.ComponentModel.DataAnnotations;

namespace DisputeDesk.Infrastructure.Outbox;

/// <summary>The <c>Kafka</c> configuration section (AD-26). It holds an address only; no credential.</summary>
public sealed class KafkaOptions
{
    public const string SectionName = "Kafka";

    /// <summary>The broker: <c>localhost:19092</c> for a Worker run on the host, <c>redpanda:9092</c> in Compose.</summary>
    [Required]
    public string BootstrapServers { get; set; } = "";
}
