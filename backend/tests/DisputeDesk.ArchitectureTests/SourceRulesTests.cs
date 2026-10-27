using System.Text.RegularExpressions;

namespace DisputeDesk.ArchitectureTests;

/// <summary>Rules the compiler and analysers cannot enforce, checked against the production source.</summary>
public partial class SourceRulesTests
{
    [Fact]
    public void ProductionSource_NullForgivingJustificationComment_IsAbsent()
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
    public void ProductionSourceFiles_Scan_FindsResultCs()
    {
        Assert.Contains(ProductionSourceFiles(), file => file.EndsWith("Result.cs", StringComparison.Ordinal));
    }

    private static string SourceRoot => SourceRoots.Src;

    private static IEnumerable<string> ProductionSourceFiles() =>
        Directory.EnumerateFiles(SourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !SourceRoots.IsBuildOutput(file));

    [GeneratedRegex(@"^\s*//\s*!")]
    private static partial Regex NullForgivingJustification();
}
