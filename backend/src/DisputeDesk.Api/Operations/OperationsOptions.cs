namespace DisputeDesk.Api.Operations;

/// <summary>
/// The <c>Operations</c> section (AD-26): <c>Operations:AllowedClients</c> lists the token <c>sub</c>s of the service
/// accounts allowed to call the operations routes, each as an operations actor (AD-9). Empty by default, and an empty
/// list refuses every token (deny by default). Locally the provisioner writes it to zitadel-clients.json; no
/// appsettings*.json holds it, because the binder merges arrays by index across configuration sources.
/// </summary>
public sealed class OperationsOptions
{
    public const string Section = "Operations";

    public string[] AllowedClients { get; set; } = [];
}
