using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DisputeDesk.ArchitectureTests;

/// <summary>
/// No <c>using X = ...;</c> directive in the backend source, tests or stubs, migrations included, and no MSBuild
/// <c>&lt;Using Include="..." Alias="..." /&gt;</c> item, which generates one (docs/conventions/backend.md; Microsoft
/// Learn, Common MSBuild project items: <c>Using</c>). Only where two types still share a name does a file import one
/// side and write the other qualified from the DisputeDesk root, which C# resolves against each enclosing namespace (C#
/// language specification, "Namespace and type names").
/// </summary>
public class UsingAliasRulesTests
{
    [Fact]
    public void SourceAndTests_UsingAliasDirective_IsAbsent()
    {
        var violations = SourceAndTestFiles()
            .SelectMany(UsingDirectives)
            .Where(directive => directive.Alias is not null)
            .Select(directive => SourceTrees.Describe(directive.GetLocation()))
            .ToList();

        Assert.True(violations.Count == 0, "Using alias directives found:\n" + string.Join('\n', violations));
    }

    [Fact]
    public void SourceAndTests_UsingDirectives_AreFoundInThisFile()
    {
        // Proves the scan reads the using directives, so an empty violation list is not a scan that found nothing.
        string thisFile = Path.Combine(SourceRoots.Tests, "DisputeDesk.ArchitectureTests", nameof(UsingAliasRulesTests) + ".cs");

        Assert.Contains(UsingDirectives(thisFile), directive => directive.Name?.ToString() == "System.Xml.Linq");
    }

    [Fact]
    public void SourceAndTestFiles_Scan_IncludesMigrationsTestsAndStubsButNoBuildOutput()
    {
        List<string> files = [.. SourceAndTestFiles()];

        Assert.Contains(files, file => file.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
        Assert.Contains(files, file => file.EndsWith(nameof(UsingAliasRulesTests) + ".cs", StringComparison.Ordinal));
        Assert.Contains(files, file => file.StartsWith(SourceRoots.Stubs + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        Assert.DoesNotContain(files, file => file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }

    [Fact]
    public void ProjectAndPropsFiles_MsBuildUsingAlias_IsAbsent()
    {
        var violations = ProjectAndPropsFiles()
            .SelectMany(file => MsBuildUsingAliases(XDocument.Load(file, LoadOptions.SetLineInfo))
                .Select(item => $"{Path.GetRelativePath(SourceRoots.Repo, file)}:{((IXmlLineInfo)item).LineNumber}: {item}"))
            .ToList();

        Assert.True(violations.Count == 0, "MSBuild Using items with an Alias found:\n" + string.Join('\n', violations));
    }

    [Fact]
    public void ProjectAndPropsFiles_Scan_IncludesEveryProjectAndTheDirectoryProps()
    {
        List<string> files = [.. ProjectAndPropsFiles().Select(file => Path.GetFileName(file))];

        Assert.Contains("Directory.Build.props", files);
        Assert.Contains("DisputeDesk.Api.csproj", files);
        Assert.Contains("DisputeDesk.CoreBankingStub.csproj", files);
        Assert.Contains("DisputeDesk.ArchitectureTests.csproj", files);
    }

    [Theory]
    [InlineData("<Using Include=\"DisputeDesk.Contracts.Disputes\" Alias=\"Wire\" />", true)]
    [InlineData("<Using Alias=\"Wire\" Include=\"DisputeDesk.Contracts.Disputes\" />", true)]
    [InlineData("<Using Include=\"Xunit\" />", false)]
    [InlineData("<Using Include=\"System.Math\" Static=\"true\" />", false)]
    [InlineData("<using Include=\"DisputeDesk.Contracts.Disputes\" alias=\"Wire\" />", true)]
    [InlineData("<Using Include=\"DisputeDesk.Contracts.Disputes\"><Alias>Wire</Alias></Using>", true)]
    [InlineData("<USING Include=\"DisputeDesk.Contracts.Disputes\"><alias>Wire</alias></USING>", true)]
    [InlineData("<Using\n    Include=\"DisputeDesk.Contracts.Disputes\"\n    Alias=\"Wire\" />", true)]
    [InlineData("<!-- shared --> <Using Include=\"DisputeDesk.Contracts.Disputes\" Alias=\"Wire\" />", true)]
    [InlineData("<!-- <Using Include=\"X\" Alias=\"Y\" /> -->", false)]
    [InlineData("<!--\n  <Using Include=\"X\" Alias=\"Y\" />\n-->", false)]
    [InlineData("<Compile Include=\"Alias.cs\" Alias=\"Wire\" />", false)]
    public void MsBuildUsingAlias_SampleItemGroup_IsMatchedOnlyForAnAlias(string items, bool expected)
    {
        var project = XDocument.Parse($"<Project><ItemGroup>{items}</ItemGroup></Project>");

        Assert.Equal(expected, MsBuildUsingAliases(project).Any());
    }

    private static IEnumerable<UsingDirectiveSyntax> UsingDirectives(string file) =>
        SourceTrees.Root(file).DescendantNodes().OfType<UsingDirectiveSyntax>();

    private static IEnumerable<string> SourceAndTestFiles() =>
        new[] { SourceRoots.Src, SourceRoots.Tests, SourceRoots.Stubs }
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(file => !SourceRoots.IsBuildOutput(file));

    private static IEnumerable<string> ProjectAndPropsFiles() =>
        Directory.EnumerateFiles(SourceRoots.Backend, "*.csproj", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(SourceRoots.Backend, "*.props", SearchOption.AllDirectories))
            .Where(file => !SourceRoots.IsBuildOutput(file));

    // `Using` items with `Alias` metadata, written as an attribute or as a child element (Microsoft Learn, Item element
    // (MSBuild): "Specify metadata as attributes"). The XML parser skips comments and joins multi-line elements. Names are
    // compared ignoring case, because MSBuild item and metadata names are not case-sensitive (Microsoft Learn, MSBuild
    // items: "Metadata name matching is case-insensitive"; MSBuild, Project file).
    private static IEnumerable<XElement> MsBuildUsingAliases(XDocument project) =>
        project.Descendants()
            .Where(element => IsNamed(element.Name, "Using"))
            .Where(element => element.Attributes().Any(attribute => IsNamed(attribute.Name, "Alias"))
                || element.Elements().Any(child => IsNamed(child.Name, "Alias")));

    private static bool IsNamed(XName name, string expected) =>
        string.Equals(name.LocalName, expected, StringComparison.OrdinalIgnoreCase);
}
