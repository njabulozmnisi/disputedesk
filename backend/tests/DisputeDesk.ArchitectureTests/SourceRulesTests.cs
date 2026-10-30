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

    /// <summary>
    /// Story 5.3 AC8, AD-14a: only the SLA sweep (<c>Worker/Sla/</c>) and the seeder (<c>Infrastructure/Seeding/</c>) name
    /// <c>ActorType.Automation</c>, so no HTTP path can record an automation actor. The wire mapping in
    /// <c>DisputeWire.cs</c> only translates a recorded actor type for the outbox and the operations read.
    /// </summary>
    [Fact]
    public void ProductionSource_AutomationActorType_AppearsOnlyInTheSweepTheSeederAndTheWireMapping()
    {
        string[] allowed =
        [
            Path.Combine("DisputeDesk.Worker", "Sla") + Path.DirectorySeparatorChar,
            Path.Combine("DisputeDesk.Infrastructure", "Seeding") + Path.DirectorySeparatorChar,
            Path.Combine("DisputeDesk.Application", "Disputes", "DisputeWire.cs"),
        ];
        var violations = ProductionSourceFiles()
            .Where(file => !allowed.Any(path => Path.GetRelativePath(SourceRoot, file).StartsWith(path, StringComparison.Ordinal)))
            .SelectMany(file => AutomationActorTypeLines(File.ReadAllText(file)).Select(number => $"{Path.GetRelativePath(SourceRoot, file)}:{number}"))
            .ToList();

        Assert.True(violations.Count == 0, "ActorType.Automation outside Worker/Sla/, the seeder and the wire mapping:\n" + string.Join('\n', violations));
    }

    [Theory]
    [InlineData("var actor = new Actor(ActorType.Automation, \"x\");", new[] { 1 })]
    [InlineData("Domain.Disputes.ActorType.Automation => ActorType.Automation,", new[] { 1, 1 })]
    [InlineData("ActorType . Automation", new[] { 1 })]
    [InlineData("var a = 1;\nnew Actor(ActorType\n    .Automation, \"x\");", new[] { 2 })]
    [InlineData("new Actor(ActorType.\r\n    Automation, \"x\");", new[] { 1 })]
    [InlineData("ActorType.AutomationKind", new int[0])]
    [InlineData("ActorType . AutomationKind", new int[0])]
    [InlineData("OtherActorType.Automation", new int[0])]
    [InlineData("// ActorType.Automation in a comment", new int[0])]
    [InlineData("/// <c>ActorType . Automation</c> in a doc comment", new int[0])]
    public void AutomationActorTypeLines_SampleSource_FindsTheEnumMemberWhateverItsSpacing(string source, int[] expectedLines)
    {
        Assert.Equal(expectedLines, AutomationActorTypeLines(source));
    }

    [Fact]
    public void ProductionSource_AutomationActorType_IsFoundInTheSweep()
    {
        // Proves the scan reads the file the rule exempts, so an empty violation list is not a scan that found nothing.
        string sweep = File.ReadAllText(Path.Combine(SourceRoot, "DisputeDesk.Worker", "Sla", "SlaSweep.cs"));

        Assert.NotEmpty(AutomationActorTypeLines(sweep));
    }

    /// <summary>
    /// The line of each <c>ActorType.Automation</c> in <paramref name="source"/>, with any spacing or line break around the
    /// dot. Line comments are blanked first, keeping their line breaks, so the line numbers stay right.
    /// </summary>
    private static List<int> AutomationActorTypeLines(string source)
    {
        string code = LineComment().Replace(source, "");
        return [.. AutomationActorType().Matches(code).Select(match => code[..match.Index].Count(c => c == '\n') + 1)];
    }

    [GeneratedRegex(@"\bActorType\s*\.\s*Automation\b")]
    private static partial Regex AutomationActorType();

    [GeneratedRegex(@"//[^\n]*")]
    private static partial Regex LineComment();

    private static string SourceRoot => SourceRoots.Src;

    private static IEnumerable<string> ProductionSourceFiles() =>
        Directory.EnumerateFiles(SourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !SourceRoots.IsBuildOutput(file));

    [GeneratedRegex(@"^\s*//\s*!")]
    private static partial Regex NullForgivingJustification();
}
