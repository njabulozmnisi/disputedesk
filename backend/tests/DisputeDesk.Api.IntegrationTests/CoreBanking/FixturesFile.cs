using System.Text.Json;

namespace DisputeDesk.Api.IntegrationTests.CoreBanking;

/// <summary>The repository's <c>fixtures/local.json</c>, the same file the stub reads.</summary>
public static class FixturesFile
{
    public static JsonDocument Read() => JsonDocument.Parse(ReadText());

    public static string ReadText() => File.ReadAllText(FindPath());

    private static string FindPath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "fixtures", "local.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Could not find fixtures/local.json above the test output directory.");
    }
}
