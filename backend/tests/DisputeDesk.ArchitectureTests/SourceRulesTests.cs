using System.Text.RegularExpressions;

namespace DisputeDesk.ArchitectureTests;

/// <summary>Rules the compiler and analysers cannot enforce, checked against the production source.</summary>
public partial class SourceRulesTests
{
    [Fact]
    public void Production_code_has_no_null_forgiving_justification_comments()
    {
        // Nullable.Extended.Analyzer skips a `!` that has a `// ! <reason>` comment above it,
        // which would quietly bypass the no-null-forgiving rule.
        var violations = ProductionSourceFiles()
            .SelectMany(file => File.ReadLines(file).Select((line, index) => (file, line, number: index + 1)))
            .Where(entry => NullForgivingJustification().IsMatch(entry.line))
            .Select(entry => $"{Path.GetRelativePath(SourceRoot, entry.file)}:{entry.number}: {entry.line.Trim()}")
            .ToList();

        Assert.True(violations.Count == 0, "Null-forgiving justification comments found:\n" + string.Join('\n', violations));
    }

    [Fact]
    public void The_scan_finds_the_production_source()
    {
        Assert.Contains(ProductionSourceFiles(), file => file.EndsWith("Result.cs", StringComparison.Ordinal));
    }

    private static string SourceRoot { get; } = Path.Combine(FindBackendRoot(), "src");

    private static IEnumerable<string> ProductionSourceFiles() =>
        Directory.EnumerateFiles(SourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file));

    private static bool IsBuildOutput(string file)
    {
        var parts = Path.GetRelativePath(SourceRoot, file).Split(Path.DirectorySeparatorChar);
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

    [GeneratedRegex(@"^\s*//\s*!")]
    private static partial Regex NullForgivingJustification();
}
