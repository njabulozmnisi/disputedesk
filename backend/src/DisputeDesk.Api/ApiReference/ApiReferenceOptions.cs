namespace DisputeDesk.Api.ApiReference;

/// <summary>
/// The <c>Scalar</c> section (AD-26): <c>Scalar:Enabled</c> maps Scalar and the runtime OpenAPI document. Off when absent
/// (<c>appsettings.json</c> sets it false); a value that is not a boolean fails start. Not named <c>ScalarOptions</c>,
/// which Scalar.AspNetCore already defines.
/// </summary>
public sealed class ApiReferenceOptions
{
    public const string Section = "Scalar";

    public bool Enabled { get; set; }
}
