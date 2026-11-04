using System.Reflection;
using DisputeDesk.Application.Abstractions;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DisputeDesk.ArchitectureTests;

/// <summary>
/// AC9, AD-11: the <c>Ownership</c> filter is bypassed only in <c>DisputeDesk.Application/Operations/</c> and
/// <c>DisputeDesk.Worker/Sla/</c>, no context is pooled (a pooled context would keep one request's customer for the
/// next; banned in <c>backend/BannedSymbols.txt</c>), and the filter reads a nullable value on the context instead of
/// <c>CurrentCustomer.CustomerId</c>, which throws when unset. 5.1 AC8: a bypass always names the filter, since the
/// parameterless form would drop every other filter too, and the customer features never reach into the bypass folders.
/// </summary>
public class OwnershipRulesTests
{
    // AD-11 names exactly these two folders, each in one project.
    private static readonly string[] BypassFolders =
    [
        Path.Combine("DisputeDesk.Application", "Operations") + Path.DirectorySeparatorChar,
        Path.Combine("DisputeDesk.Worker", "Sla") + Path.DirectorySeparatorChar,
    ];

    // The customer feature folders, in any project.
    private static readonly string[] CustomerFeatureFolders = ["Disputes", "Transactions"];

    // Each project's Operations and Sla namespaces, written in full or from the project name. ActorType.Operations, the
    // enum member, is not among them because ActorType is not a project, and a bare Sla is the dispute's SLA constant.
    private static readonly HashSet<string> OperationsAndSlaNamespaces =
    [
        .. new[] { "Api", "Application", "Contracts", "Domain", "Infrastructure", "Worker" }
            .SelectMany(project => new[] { $"{project}.Operations", $"{project}.Sla" })
            .SelectMany(name => new[] { name, "DisputeDesk." + name }),
    ];

    [Fact]
    public void ProductionSource_IgnoreQueryFilters_AppearsOnlyInOperationsAndSla()
    {
        var violations = SourceFiles()
            .Where(file => !IsInBypassFolder(file))
            .SelectMany(IgnoreQueryFiltersCalls)
            .Select(call => SourceTrees.Describe(call.GetLocation()))
            .ToList();

        Assert.True(violations.Count == 0, "IgnoreQueryFilters outside Operations/ and Sla/:\n" + string.Join('\n', violations));
    }

    [Fact]
    public void ProductionSource_ParameterlessIgnoreQueryFilters_IsAbsent()
    {
        var violations = SourceFiles()
            .SelectMany(IgnoreQueryFiltersCalls)
            .Where(call => call.ArgumentList.Arguments.Count == 0)
            .Select(call => SourceTrees.Describe(call.GetLocation()))
            .ToList();

        Assert.True(violations.Count == 0, "IgnoreQueryFilters() without the filter's name:\n" + string.Join('\n', violations));
    }

    [Fact]
    public void ProductionSource_NamedIgnoreQueryFilters_IsFoundInOperations()
    {
        // Proves both IgnoreQueryFilters scans read the calls the rules allow, so an empty violation list is not a scan
        // that found nothing.
        string readDispute = Path.Combine(SourceRoots.Src, "DisputeDesk.Application", "Operations", "ReadDispute", "ReadDisputeHandler.cs");

        Assert.Contains(IgnoreQueryFiltersCalls(readDispute), call => call.ArgumentList.Arguments.Count == 1);
    }

    [Fact]
    public void CustomerFeatureSource_OperationsOrSlaNamespace_IsNeverReferenced()
    {
        var violations = SourceFiles()
            .Where(IsInCustomerFeatureFolder)
            .SelectMany(OperationsOrSlaNamespaceReferences)
            .Select(node => SourceTrees.Describe(node.GetLocation()))
            .ToList();

        Assert.True(violations.Count == 0, "Disputes/ or Transactions/ referencing Operations or Sla:\n" + string.Join('\n', violations));
    }

    [Fact]
    public void ProductionSource_OperationsNamespace_IsFoundInTheOperationsEndpoints()
    {
        // Proves the namespace scan reads the references the rule allows outside the customer feature folders.
        string endpoints = Path.Combine(SourceRoots.Src, "DisputeDesk.Api", "Operations", "OperationsEndpoints.cs");

        Assert.NotEmpty(OperationsOrSlaNamespaceReferences(endpoints));
    }

    [Theory]
    [InlineData("Application/Disputes/GetDispute/GetDisputeHandler.cs", true)]
    [InlineData("Api/Transactions/TransactionEndpoints.cs", true)]
    [InlineData("Domain/Disputes/Dispute.cs", true)]
    [InlineData("Application/Operations/ReadDispute/ReadDisputeHandler.cs", false)]
    [InlineData("Application/Abstractions/CurrentCustomer.cs", false)]
    public void IsInCustomerFeatureFolder_SamplePath_IsTrueOnlyForDisputesAndTransactions(string relativePath, bool expected)
    {
        Assert.Equal(expected, IsInCustomerFeatureFolder(Path.Combine(SourceRoots.Src, "DisputeDesk." + relativePath)));
    }

    [Theory]
    [InlineData("Application/Operations/StartReview/StartReviewHandler.cs", true)]
    [InlineData("Worker/Sla/SlaSweep.cs", true)]
    [InlineData("Application/Disputes/RaiseDispute/RaiseDisputeHandler.cs", false)]
    [InlineData("Application/OperationsHelpers/Lookup.cs", false)]
    [InlineData("Application/Disputes/Operations/Lookup.cs", false)] // an Operations folder elsewhere in Application
    [InlineData("Api/Operations/OperationsEndpoints.cs", false)]     // Operations/ in another project
    [InlineData("Infrastructure/Sla/SlaQueries.cs", false)]          // Sla/ outside the Worker
    [InlineData("Worker/Outbox/Sla/Relay.cs", false)]                 // Sla/ deeper in the Worker
    public void IsInBypassFolder_SamplePath_IsTrueOnlyForTheTwoFolders(string relativePath, bool expected)
    {
        Assert.Equal(expected, IsInBypassFolder(Path.Combine(SourceRoots.Src, "DisputeDesk." + relativePath)));
    }

    [Fact]
    public void AppDbContext_OwnershipFilter_ReadsANullableCustomerIdOnTheContext()
    {
        PropertyInfo? parameter = typeof(AppDbContext).GetProperty("CurrentCustomerId", BindingFlags.Instance | BindingFlags.NonPublic);
        SyntaxNode context = SourceTrees.Root(Path.Combine(SourceRoots.Src, "DisputeDesk.Infrastructure", "Persistence", "AppDbContext.cs"));
        List<string> readFromTheCustomer =
            [.. context.DescendantNodes().OfType<SimpleNameSyntax>().Where(IsReadFromTheCurrentCustomer).Select(name => name.Identifier.ValueText)];

        Assert.NotNull(parameter);
        Assert.Equal(typeof(string), parameter.PropertyType);
        Assert.Contains(nameof(CurrentCustomer.CustomerIdOrNull), readFromTheCustomer);
        // CurrentCustomer.CustomerId, unlike CustomerIdOrNull, throws when no customer is resolved.
        Assert.DoesNotContain(nameof(CurrentCustomer.CustomerId), readFromTheCustomer);
    }

    private static bool IsInBypassFolder(string file) =>
        BypassFolders.Any(folder => Path.GetRelativePath(SourceRoots.Src, file).StartsWith(folder, StringComparison.Ordinal));

    /// <summary>Whether <paramref name="file"/> is in a project's top-level <c>Disputes/</c> or <c>Transactions/</c> folder.</summary>
    private static bool IsInCustomerFeatureFolder(string file)
    {
        string[] parts = Path.GetRelativePath(SourceRoots.Src, file).Split(Path.DirectorySeparatorChar);
        return parts.Length > 2 && CustomerFeatureFolders.Contains(parts[1]);
    }

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(SourceRoots.Src, "*.cs", SearchOption.AllDirectories)
            .Where(file => !SourceRoots.IsBuildOutput(file));

    private static IEnumerable<InvocationExpressionSyntax> IgnoreQueryFiltersCalls(string file) =>
        SourceTrees.Root(file).DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(call => SourceTrees.LastName(call.Expression) == "IgnoreQueryFilters");

    /// <summary>
    /// Each name in <paramref name="file"/> that is, or starts with, an Operations or Sla namespace of a project, in a
    /// using directive, a qualified name or a member access. The whole name from its start is compared, so a project name
    /// inside another namespace (<c>Vendor.Api.Operations</c>, <c>PartnerApi.Operations</c>) never matches.
    /// </summary>
    private static IEnumerable<SyntaxNode> OperationsOrSlaNamespaceReferences(string file) =>
        SourceTrees.Root(file).DescendantNodes()
            .Where(node => node is QualifiedNameSyntax or MemberAccessExpressionSyntax
                && DottedName(node) is { } name
                && OperationsAndSlaNamespaces.Contains(name));

    /// <summary>The name a node spells, such as <c>DisputeDesk.Application.Operations</c>, or null when a part is not a plain name.</summary>
    private static string? DottedName(SyntaxNode node) => node switch
    {
        IdentifierNameSyntax name => name.Identifier.ValueText,
        AliasQualifiedNameSyntax { Alias.Identifier.ValueText: "global" } name => name.Name.Identifier.ValueText,
        QualifiedNameSyntax name => Join(DottedName(name.Left), name.Right),
        MemberAccessExpressionSyntax access => Join(DottedName(access.Expression), access.Name),
        _ => null,
    };

    private static string? Join(string? left, SimpleNameSyntax right) => left is null ? null : $"{left}.{right.Identifier.ValueText}";

    /// <summary>Whether <paramref name="member"/> is read from a current customer: <c>currentCustomer.X</c> or <c>currentCustomer?.X</c>.</summary>
    private static bool IsReadFromTheCurrentCustomer(SimpleNameSyntax member)
    {
        SyntaxNode? receiver = member.Parent switch
        {
            MemberAccessExpressionSyntax access when access.Name == member => access.Expression,
            MemberBindingExpressionSyntax => member.FirstAncestorOrSelf<ConditionalAccessExpressionSyntax>()?.Expression,
            _ => null,
        };
        return receiver is not null
            && SourceTrees.LastName(receiver) is { } name
            && name.EndsWith("CurrentCustomer", StringComparison.OrdinalIgnoreCase);
    }
}
