namespace DisputeDesk.Api.Operations;

/// <summary>
/// The <c>Operations</c> section (AD-26): <c>Operations:Actors:&lt;sub&gt;</c> maps a service account's token <c>sub</c>
/// to its actor type. AD-9 allows exactly one actor type, <c>operations</c>; start-up refuses any other value. An empty
/// map is valid, and every token is then refused. Locally the provisioner writes it to zitadel-clients.json.
/// </summary>
public sealed class OperationsOptions
{
    public const string Section = "Operations";

    /// <summary>The one actor type a token <c>sub</c> may be mapped to.</summary>
    public const string OperationsActorType = "operations";

    public Dictionary<string, string> Actors { get; set; } = [];

    /// <summary>Whether every mapping names <see cref="OperationsActorType"/> exactly.</summary>
    public bool MapsOnlyToOperations() => Actors.Values.All(actorType => actorType == OperationsActorType);
}
