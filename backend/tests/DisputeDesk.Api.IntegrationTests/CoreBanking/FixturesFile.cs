using System.Text.Json;
using DisputeDesk.Api.IntegrationTests.Infrastructure;

namespace DisputeDesk.Api.IntegrationTests.CoreBanking;

/// <summary>The repository's <c>backend/fixtures/local.json</c>, the same file the stub reads.</summary>
public static class FixturesFile
{
    public static JsonDocument Read() => JsonDocument.Parse(ReadText());

    public static string ReadText() => File.ReadAllText(FindPath());

    public static string FindPath() => Path.Combine(RepoRoot.Backend, "fixtures", "local.json");
}
