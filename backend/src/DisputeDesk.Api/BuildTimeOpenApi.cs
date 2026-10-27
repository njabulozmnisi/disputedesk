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
}
