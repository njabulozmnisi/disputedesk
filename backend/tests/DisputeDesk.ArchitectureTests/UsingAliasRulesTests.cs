using System.Text.RegularExpressions;

namespace DisputeDesk.ArchitectureTests;

/// <summary>
/// No <c>using X = ...;</c> directive in the backend source or tests, migrations included (docs/conventions/backend.md).
/// A file imports one side, Domain or Contracts, and writes a type of the other qualified from the DisputeDesk root,
/// which C# resolves against each enclosing namespace (C# language specification, "Namespace and type names").
/// </summary>
public partial class UsingAliasRulesTests
{
    [Fact]
    public void SourceAndTests_UsingAliasDirective_IsAbsent()
    {
        var violations = SourceAndTestFiles()
            .SelectMany(file => File.ReadLines(file).Select((line, index) => (file, line, number: index + 1)))
            .Where(entry => UsingAliasDirective().IsMatch(entry.line))
            .Select(entry => $"{Path.GetRelativePath(SourceRoots.Repo, entry.file)}:{entry.number}: {entry.line.Trim()}")
            .ToList();

        Assert.True(violations.Count == 0, "Using alias directives found:\n" + string.Join('\n', violations));
    }

    [Fact]
    public void SourceAndTestFiles_Scan_IncludesMigrationsAndTestsButNoBuildOutput()
    {
        List<string> files = [.. SourceAndTestFiles()];

        Assert.Contains(files, file => file.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
        Assert.Contains(files, file => file.EndsWith(nameof(UsingAliasRulesTests) + ".cs", StringComparison.Ordinal));
        Assert.DoesNotContain(files, file => file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("using Wire = DisputeDesk.Contracts.Disputes;", true)]
    [InlineData("using ActorType = DisputeDesk.Domain.Disputes.ActorType;", true)]
    [InlineData("global using Json = System.Text.Json;", true)]
    [InlineData("    using Pair = (int Left, int Right);", true)]
    [InlineData("using Names = System.Collections.Generic.List<string>;", true)]
    [InlineData("using DisputeDesk.Domain.Disputes;", false)]
    [InlineData("global using global::System;", false)]
    [InlineData("using static System.Math;", false)]
    [InlineData("using var activity = Source.StartActivity();", false)]
    [InlineData("using IServiceScope scope = provider.CreateScope();", false)]
    [InlineData("using (var scope = provider.CreateScope())", false)]
    [InlineData("// using Wire = DisputeDesk.Contracts.Disputes;", false)]
    public void UsingAliasDirective_SampleLine_IsMatchedOnlyForAnAlias(string line, bool expected)
    {
        Assert.Equal(expected, UsingAliasDirective().IsMatch(line));
    }

    private static IEnumerable<string> SourceAndTestFiles() =>
        new[] { SourceRoots.Src, SourceRoots.Tests }
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(file => !SourceRoots.IsBuildOutput(file));

    // `using` (or `global using`), one name, then `=`: an alias. A using declaration (`using var x =`,
    // `using Type x =`) has two names before the `=`, and a using statement opens a parenthesis.
    [GeneratedRegex(@"^\s*(?:global\s+)?using\s+@?\w+\s*=")]
    private static partial Regex UsingAliasDirective();
}
