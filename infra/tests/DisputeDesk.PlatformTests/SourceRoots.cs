namespace DisputeDesk.PlatformTests;

/// <summary>Where the files under test live, for the tests that read them.</summary>
internal static class SourceRoots
{
    /// <summary>The repository root, where infra/ and docker-compose.yml live.</summary>
    public static string Repo { get; } = FindRepoRoot();

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "docker-compose.yml")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Could not find docker-compose.yml above the test output directory.");
    }
}
