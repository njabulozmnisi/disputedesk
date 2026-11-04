using System.Reflection;
using System.Text.RegularExpressions;
using DisputeDesk.Contracts.Problems;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DisputeDesk.ArchitectureTests;

/// <summary>
/// AD-3: the problem slugs are constants in Contracts, one class per feature, and every error code Domain and
/// Application create is one of them.
/// </summary>
public partial class ProblemCatalogueTests
{
    private static readonly string[] Ad3Slugs =
    [
        "validation", "not-found", "action-not-allowed", "already-disputed", "not-disputable",
        "idempotency-in-progress", "idempotency-mismatch", "linking-required", "already-linked", "otp-incorrect",
        "linking-locked", "rate-limited", "core-banking-unavailable", "unauthorized", "forbidden", "internal",
    ];

    // The Error factories of Domain/Error.cs, each taking the code first.
    private static readonly string[] ErrorFactories =
        ["NotFound", "Validation", "Forbidden", "ActionNotAllowed", "Conflict", "BusinessRule", "Unavailable", "RateLimited"];

    [Fact]
    public void Catalogue_AllFeatureClasses_HoldExactlyTheAd3Slugs()
    {
        List<string> catalogue = CatalogueSlugs();

        Assert.Equal(catalogue.Count, catalogue.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(Ad3Slugs.Order(StringComparer.Ordinal), catalogue.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void For_Slug_ReturnsTheUrlOfItsPage()
    {
        Assert.Equal("https://github.com/njabulozmnisi/disputedesk/blob/main/docs/problems/not-found.md", ProblemType.For("not-found"));
    }

    [Fact]
    public void Catalogue_EverySlug_HasAPageThatNamesItsTypeAndStatus()
    {
        var violations = new List<string>();

        foreach (string slug in CatalogueSlugs())
        {
            string path = Path.Combine(SourceRoots.Repo, "docs", "problems", slug + ".md");
            if (!File.Exists(path))
            {
                violations.Add($"{slug}: no page at docs/problems/{slug}.md");
                continue;
            }

            string page = File.ReadAllText(path);
            if (!page.Contains(ProblemType.For(slug), StringComparison.Ordinal))
            {
                violations.Add($"{slug}: the page does not name its type {ProblemType.For(slug)}");
            }

            if (!StatusLine().IsMatch(page))
            {
                violations.Add($"{slug}: the page has no '- Status: ' line starting with a 3-digit HTTP status");
            }
        }

        Assert.True(violations.Count == 0, "Problem pages missing or out of date:\n" + string.Join('\n', violations));
    }

    [Theory]
    [InlineData("- Status: 409", true)]
    [InlineData("- Status: 400, or another 4xx the request itself causes", true)]
    [InlineData("- Status: decided later", false)]
    [InlineData("- Status: 4000", false)]
    public void StatusLine_SamplePage_MatchesOnlyALeadingHttpStatus(string line, bool expected)
    {
        string page = "# Title\n\n- Type: `x`\n" + line + "\n";

        Assert.Equal(expected, StatusLine().IsMatch(page));
    }

    [Fact]
    public void ErrorCodes_DomainAndApplication_AreAllInTheCatalogue()
    {
        List<string> catalogue = CatalogueSlugs();
        var violations = new List<string>();

        foreach (string file in SourceFiles("DisputeDesk.Domain", "DisputeDesk.Application"))
        {
            foreach (InvocationExpressionSyntax call in ErrorFactoryCalls(file))
            {
                bool isKnownLiteral = Code(call) is LiteralExpressionSyntax literal
                    && literal.IsKind(SyntaxKind.StringLiteralExpression)
                    && catalogue.Contains(literal.Token.ValueText);
                if (!isKnownLiteral)
                {
                    violations.Add(SourceTrees.Describe(call.GetLocation()));
                }
            }
        }

        Assert.True(violations.Count == 0, "Error codes outside the AD-3 catalogue (use a string literal from the catalogue):\n" + string.Join('\n', violations));
    }

    [Fact]
    public void DomainSource_ErrorFactoryCall_IsFoundWithItsCode()
    {
        // Proves the scan reads the calls and their code argument, so an empty violation list is not a scan that found nothing.
        string disputeErrors = Path.Combine(SourceRoots.Src, "DisputeDesk.Domain", "Disputes", "DisputeErrors.cs");

        Assert.Contains(ErrorFactoryCalls(disputeErrors), call => Code(call) is LiteralExpressionSyntax { Token.ValueText: "not-found" });
    }

    /// <summary>Every public string constant of the <c>*Problems</c> classes in Contracts.</summary>
    private static List<string> CatalogueSlugs() =>
        typeof(ProblemType).Assembly.GetTypes()
            .Where(type => type.Namespace == typeof(ProblemType).Namespace && type.Name.EndsWith("Problems", StringComparison.Ordinal))
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToList();

    private static IEnumerable<string> SourceFiles(params string[] projects) =>
        projects.SelectMany(project => Directory.EnumerateFiles(Path.Combine(SourceRoots.Src, project), "*.cs", SearchOption.AllDirectories))
            .Where(file => !SourceRoots.IsBuildOutput(file));

    /// <summary>Each <c>Error.&lt;Kind&gt;(code, …)</c> factory call in <paramref name="file"/>.</summary>
    private static IEnumerable<InvocationExpressionSyntax> ErrorFactoryCalls(string file) =>
        SourceTrees.Root(file).DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(call => call.Expression is MemberAccessExpressionSyntax access
                && SourceTrees.LastName(access.Expression) == "Error"
                && ErrorFactories.Contains(access.Name.Identifier.ValueText));

    /// <summary>The code argument of a factory call, its first; null when the call has none.</summary>
    private static ExpressionSyntax? Code(InvocationExpressionSyntax call) => call.ArgumentList.Arguments.FirstOrDefault()?.Expression;

    /// <summary>A <c>- Status: </c> line that starts with a 3-digit HTTP status (<c>409</c>, <c>400, or another 4xx…</c>).</summary>
    [GeneratedRegex(@"^- Status: \d{3}\b", RegexOptions.Multiline)]
    private static partial Regex StatusLine();
}
