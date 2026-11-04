using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DisputeDesk.ArchitectureTests;

/// <summary>Rules the compiler and analysers cannot enforce, checked against the production source.</summary>
public class SourceRulesTests
{
    [Fact]
    public void ProductionSource_NullForgivingJustificationComment_IsAbsent()
    {
        // Nullable.Extended.Analyzer skips a `!` that has a `// ! <reason>` comment above it,
        // which would quietly bypass the no-null-forgiving rule.
        var violations = ProductionSourceFiles()
            .SelectMany(LineComments)
            .Where(comment => comment.ToString()[2..].TrimStart().StartsWith('!'))
            .Select(comment => SourceTrees.Describe(comment.GetLocation()))
            .ToList();

        Assert.True(violations.Count == 0, "Null-forgiving justification comments found:\n" + string.Join('\n', violations));
    }

    [Fact]
    public void ProductionSource_LineComments_AreFoundInTheOutboxRelay()
    {
        // Proves the scan reads comment trivia, so an empty violation list is not a scan that found nothing.
        string relay = Path.Combine(SourceRoot, "DisputeDesk.Infrastructure", "Outbox", "OutboxRelay.cs");

        Assert.Contains(LineComments(relay), comment => comment.ToString().StartsWith("// shortcut:", StringComparison.Ordinal));
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
            .SelectMany(AutomationActorTypes)
            .Select(node => SourceTrees.Describe(node.GetLocation()))
            .ToList();

        Assert.True(violations.Count == 0, "ActorType.Automation outside Worker/Sla/, the seeder and the wire mapping:\n" + string.Join('\n', violations));
    }

    [Fact]
    public void ProductionSource_AutomationActorType_IsFoundInTheSweep()
    {
        // Proves the scan reads the file the rule exempts, so an empty violation list is not a scan that found nothing.
        Assert.NotEmpty(AutomationActorTypes(Path.Combine(SourceRoot, "DisputeDesk.Worker", "Sla", "SlaSweep.cs")));
    }

    private static IEnumerable<SyntaxTrivia> LineComments(string file) =>
        SourceTrees.Root(file).DescendantTrivia().Where(trivia => trivia.IsKind(SyntaxKind.SingleLineCommentTrivia));

    /// <summary>
    /// Each <c>ActorType.Automation</c> in <paramref name="file"/>, qualified or not: a member access in an expression or a
    /// pattern, or a qualified name, which is how the parser reads <c>x is ActorType.Automation</c>.
    /// </summary>
    private static IEnumerable<SyntaxNode> AutomationActorTypes(string file) =>
        SourceTrees.Root(file).DescendantNodes().Where(node => node switch
        {
            MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText == "Automation" && SourceTrees.LastName(access.Expression) == "ActorType",
            QualifiedNameSyntax name => name.Right.Identifier.ValueText == "Automation" && SourceTrees.LastName(name.Left) == "ActorType",
            _ => false,
        });

    private static string SourceRoot => SourceRoots.Src;

    private static IEnumerable<string> ProductionSourceFiles() =>
        Directory.EnumerateFiles(SourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !SourceRoots.IsBuildOutput(file));
}
