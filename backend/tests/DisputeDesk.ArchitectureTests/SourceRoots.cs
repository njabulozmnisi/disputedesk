namespace DisputeDesk.ArchitectureTests;

/// <summary>Where the production source lives, for the tests that read it.</summary>
internal static class SourceRoots
{
    public static string Src { get; } = Path.Combine(FindBackendRoot(), "src");

    public static string Tests { get; } = Path.Combine(FindBackendRoot(), "tests");

    /// <summary>The core-banking stub (AD-21), a separate minimal API under backend/stubs.</summary>
    public static string Stubs { get; } = Path.Combine(FindBackendRoot(), "stubs");

    /// <summary>backend/, where the solution, the Directory.*.props files and every project live.</summary>
    public static string Backend { get; } = FindBackendRoot();

    /// <summary>The repository root, above backend/, where infra/, scripts/ and docker-compose.yml live.</summary>
    public static string Repo { get; } = Path.GetFullPath(Path.Combine(FindBackendRoot(), ".."));

    public static bool IsBuildOutput(string file)
    {
        string[] parts = Path.GetRelativePath(Src, file).Split(Path.DirectorySeparatorChar);
        return parts.Contains("bin") || parts.Contains("obj");
    }

    private static string FindBackendRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DisputeDesk.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Could not find backend/DisputeDesk.slnx above the test output directory.");
    }
}
