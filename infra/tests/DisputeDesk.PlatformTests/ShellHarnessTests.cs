namespace DisputeDesk.PlatformTests;

/// <summary>Infra's tests are xUnit tests in this project, not shell harnesses (INF-1).</summary>
public sealed class ShellHarnessTests
{
    [Fact]
    public void Infra_HoldsNoShellTestHarness()
    {
        // The one exception, until JSON policy checks in this project replace it. infra/tests/smoke.sh, the
        // developer smoke check, is not a harness and matches neither pattern.
        string[] exceptions = ["infra/terraform/iam-names.test.sh"];
        string infra = Path.Combine(SourceRoots.Repo, "infra");

        string[] harnesses = [.. Directory.GetFiles(infra, "*.test.sh", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Path.Combine(infra, "aws"), "check-*.sh"))
            .Select(file => Path.GetRelativePath(SourceRoots.Repo, file).Replace('\\', '/'))
            .Except(exceptions)
            .Order(StringComparer.Ordinal)];

        Assert.True(harnesses.Length == 0, $"Shell test harnesses under infra/: {string.Join(", ", harnesses)}");
    }
}
