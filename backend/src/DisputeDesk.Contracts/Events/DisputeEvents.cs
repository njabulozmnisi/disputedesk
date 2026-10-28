namespace DisputeDesk.Contracts.Events;

/// <summary>The fixed CloudEvents attributes of dispute events (AD-19).</summary>
public static class DisputeEvents
{
    public const string SpecVersion = "1.0";
    public const string Source = "/disputedesk/api";
    public const string DataContentType = "application/json";
    public const string RaisedV1 = "disputedesk.dispute.raised.v1";

    /// <summary>Where the committed schemas are published; <see cref="SchemaFor"/> appends the file name.</summary>
    public const string SchemaPrefix = "https://github.com/njabulozmnisi/disputedesk/blob/main/contracts/events/";

    /// <summary>The <c>dataschema</c> of an event type: the resolvable URL of its committed JSON Schema.</summary>
    public static string SchemaFor(string type) => SchemaPrefix + type + ".schema.json";
}
