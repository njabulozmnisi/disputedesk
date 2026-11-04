using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DisputeDesk.ArchitectureTests;

/// <summary>
/// The C# files the source rules read, as Roslyn syntax trees (Microsoft Learn, Get started with syntax analysis), so
/// comments, strings, spacing and line breaks are the parser's job, not a pattern's.
/// </summary>
internal static class SourceTrees
{
    // One cached tree per file, shared by every rule that reads it. xUnit runs test classes in parallel, hence the
    // concurrent dictionary; two rules that ask for an uncached file at the same moment may both parse it, and GetOrAdd
    // keeps one tree.
    private static readonly ConcurrentDictionary<string, SyntaxNode> Roots = new();

    /// <summary>The root of <paramref name="file"/>'s syntax tree.</summary>
    public static SyntaxNode Root(string file) =>
        Roots.GetOrAdd(file, path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path).GetRoot());

    /// <summary><c>&lt;path from the repository root&gt;:&lt;line&gt;</c> of a node, token or trivia's location.</summary>
    public static string Describe(Location location)
    {
        FileLinePositionSpan span = location.GetLineSpan();
        return $"{Path.GetRelativePath(SourceRoots.Repo, span.Path)}:{span.StartLinePosition.Line + 1}";
    }

    /// <summary>
    /// The rightmost name of a name or member access: <c>ActorType</c> in <c>Domain.Disputes.ActorType</c>, and on an
    /// invocation's expression the method it calls (<c>Add</c> in <c>db.Add(x)</c>, <c>x?.Add()</c> or <c>Add(x)</c>).
    /// </summary>
    public static string? LastName(SyntaxNode node) => node switch
    {
        SimpleNameSyntax name => name.Identifier.ValueText,
        MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText,
        MemberBindingExpressionSyntax binding => binding.Name.Identifier.ValueText,
        QualifiedNameSyntax name => name.Right.Identifier.ValueText,
        AliasQualifiedNameSyntax name => name.Name.Identifier.ValueText,
        _ => null,
    };
}
