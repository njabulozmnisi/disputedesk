namespace DisputeDesk.Infrastructure;

/// <summary>
/// The <c>ConnectionStrings</c> configuration section, bound with the options pattern so the host that runs the outbox relay
/// and the SLA sweep checks it at start (Microsoft Learn, Options pattern: options validation). The database context reads
/// the same key through <see cref="DependencyInjection"/>.
/// </summary>
public sealed class ConnectionStringsOptions
{
    public const string SectionName = "ConnectionStrings";

    /// <summary>The PostgreSQL connection string, <c>ConnectionStrings:DisputeDesk</c>.</summary>
    public string DisputeDesk { get; set; } = "";
}
