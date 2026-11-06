namespace DisputeDesk.PlatformTests;

/// <summary>Infra's tests are xUnit tests in this project, not shell harnesses.</summary>
public sealed class ShellHarnessTests
{
    [Fact]
    public void Infra_HoldsNoShellTestHarness()
    {
        // infra/tests/smoke.sh, the developer smoke check, is not a harness and does not match the pattern.
        string infra = Path.Combine(SourceRoots.Repo, "infra");

        string[] harnesses = [.. Directory.GetFiles(infra, "*.test.sh", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(SourceRoots.Repo, file).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)];

        Assert.True(harnesses.Length == 0, $"Shell test harnesses under infra/: {string.Join(", ", harnesses)}");
    }
}
