using System.Reflection;

namespace DisputeDesk.Api;

/// <summary>
/// The build writes openapi.json by starting this host inside the GetDocument.Insider tool, with no configuration
/// and no database (Microsoft Learn: "Customize runtime behavior during build-time document generation"). While it
/// runs, the Api skips everything that would touch the database or check settings.
/// </summary>
public static class BuildTimeOpenApi
{
    public static bool IsRunning { get; } = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

    /// <summary>
    /// Stand-ins for the settings Infrastructure validates at start-up, which that run does not have. Nothing is
    /// called during the run, so the values are never used.
    /// </summary>
    public static IReadOnlyDictionary<string, string?> StandInSettings { get; } = new Dictionary<string, string?>
    {
        ["CoreBanking:BaseUrl"] = "https://core-banking.invalid",
        ["Sms:Host"] = "sms.invalid",
        ["Sms:Port"] = "25",
    };
}
