namespace DisputeDesk.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Where the repository's files live, for the tests that read them (copied from the architecture tests'
/// <c>SourceRoots</c>): <see cref="Backend"/> holds <c>openapi.json</c>, <see cref="Repo"/> holds <c>infra/</c>.
/// </summary>
internal static class RepoRoot
{
    public static string Backend { get; } = FindBackendRoot();

    /// <summary>The repository root, above backend/.</summary>
    public static string Repo { get; } = Path.GetFullPath(Path.Combine(Backend, ".."));

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
