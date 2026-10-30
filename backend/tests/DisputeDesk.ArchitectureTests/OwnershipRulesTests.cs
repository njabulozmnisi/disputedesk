using System.Reflection;
using System.Text.RegularExpressions;
using DisputeDesk.Application.Abstractions;
using DisputeDesk.Infrastructure.Persistence;

namespace DisputeDesk.ArchitectureTests;

/// <summary>
/// AC9, AD-11: the <c>Ownership</c> filter is bypassed only in <c>DisputeDesk.Application/Operations/</c> and
/// <c>DisputeDesk.Worker/Sla/</c>, no context is pooled (a pooled context would keep one request's customer for the
/// next), and the filter reads a nullable value on the context instead of <c>CurrentCustomer.CustomerId</c>, which
/// throws when unset. 5.1 AC8: a bypass always names the filter, since the parameterless form would drop every other
/// filter too, and the customer features never reach into the bypass folders.
/// </summary>
public partial class OwnershipRulesTests
{
    // AD-11 names exactly these two folders, each in one project.
    private static readonly string[] BypassFolders =
    [
        Path.Combine("DisputeDesk.Application", "Operations") + Path.DirectorySeparatorChar,
        Path.Combine("DisputeDesk.Worker", "Sla") + Path.DirectorySeparatorChar,
    ];

    // The customer feature folders, in any project.
    private static readonly string[] CustomerFeatureFolders = ["Disputes", "Transactions"];

    [Fact]
    public void ProductionSource_IgnoreQueryFilters_AppearsOnlyInOperationsAndSla()
    {
        var violations = SourceLines()
            .Where(line => IgnoreQueryFilters().IsMatch(line.Text) && !IsInBypassFolder(line.File))
            .Select(Describe)
            .ToList();

        Assert.True(violations.Count == 0, "IgnoreQueryFilters outside Operations/ and Sla/:\n" + string.Join('\n', violations));
    }

    [Fact]
    public void ProductionSource_ParameterlessIgnoreQueryFilters_IsAbsent()
    {
        var violations = SourceLines().Where(line => ParameterlessIgnoreQueryFilters().IsMatch(line.Text)).Select(Describe).ToList();

        Assert.True(violations.Count == 0, "IgnoreQueryFilters() without the filter's name:\n" + string.Join('\n', violations));
    }

    [Fact]
    public void CustomerFeatureSource_OperationsOrSlaNamespace_IsNeverReferenced()
    {
        var violations = SourceLines()
            .Where(line => IsInCustomerFeatureFolder(line.File) && OperationsOrSlaNamespace().IsMatch(line.Text))
            .Select(Describe)
            .ToList();

        Assert.True(violations.Count == 0, "Disputes/ or Transactions/ referencing Operations or Sla:\n" + string.Join('\n', violations));
    }

    [Theory]
    [InlineData("query.IgnoreQueryFilters()", true)]
    [InlineData("query.IgnoreQueryFilters( )", true)]
    [InlineData("db.Set<Dispute>().IgnoreQueryFilters([\"Ownership\"])", false)]
    [InlineData("query.IgnoreQueryFilters(filters)", false)]
    public void ParameterlessIgnoreQueryFilters_SampleLine_IsMatchedOnlyWithoutAName(string line, bool expected)
    {
        Assert.Equal(expected, ParameterlessIgnoreQueryFilters().IsMatch(line));
    }

    [Theory]
    [InlineData("using DisputeDesk.Application.Operations.ReadDispute;", true)]
    [InlineData("using DisputeDesk.Worker.Sla;", true)]
    [InlineData("Contracts.Operations.OperationsDisputeResponse response,", true)]
    [InlineData("Application.Operations.ReadDispute.ReadDisputeHandler handler,", true)]
    [InlineData("SlaDueAtUtc = raised.OccurredAtUtc + Sla;", false)] // the dispute's SLA constant
    [InlineData("Domain.Disputes.ActorType.Operations => ActorType.Operations,", false)] // the actor type, not a namespace
    [InlineData("    Operations,", false)]
    [InlineData("/// <summary>Operations started reviewing the dispute (R20).</summary>", false)]
    [InlineData("dispute.SlaDueAtUtc", false)]
    [InlineData("using Vendor.Api.Operations;", false)] // another library's namespace: rejected only by the lookbehind
    [InlineData("using PartnerApi.Operations;", false)] // a name ending in a project name: rejected only by the lookbehind
    public void OperationsOrSlaNamespace_SampleLine_IsMatchedOnlyForANamespaceReference(string line, bool expected)
    {
        Assert.Equal(expected, OperationsOrSlaNamespace().IsMatch(line));
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

    [Fact]
    public void ProductionSource_AddDbContextPool_IsAbsent()
    {
        var violations = SourceLines()
            .Where(line => line.Text.Contains("AddDbContextPool", StringComparison.Ordinal) || line.Text.Contains("AddPooledDbContextFactory", StringComparison.Ordinal))
            .Select(line => $"{Path.GetRelativePath(SourceRoots.Src, line.File)}:{line.Number}")
            .ToList();

        Assert.True(violations.Count == 0, "Pooled DbContext registration found:\n" + string.Join('\n', violations));
    }

    [Theory]
    [InlineData("db.Set<Dispute>().IgnoreQueryFilters([\"Ownership\"])", true)]
    [InlineData("query.IgnoreQueryFilters ()", true)]
    [InlineData("// the filter is not ignored here", false)]
    public void IgnoreQueryFilters_SampleLine_IsMatchedOnlyWhenCalled(string line, bool expected)
    {
        Assert.Equal(expected, IgnoreQueryFilters().IsMatch(line));
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
        string source = File.ReadAllText(Path.Combine(SourceRoots.Src, "DisputeDesk.Infrastructure", "Persistence", "AppDbContext.cs"));

        Assert.NotNull(parameter);
        Assert.Equal(typeof(string), parameter.PropertyType);
        Assert.Contains("." + nameof(CurrentCustomer.CustomerIdOrNull), source, StringComparison.Ordinal);
        // CurrentCustomer.CustomerId, unlike CustomerIdOrNull, throws when no customer is resolved.
        Assert.DoesNotMatch(@"[Cc]urrentCustomer\??\.CustomerId\b", source);
    }

    private static bool IsInBypassFolder(string file) =>
        BypassFolders.Any(folder => Path.GetRelativePath(SourceRoots.Src, file).StartsWith(folder, StringComparison.Ordinal));

    /// <summary>Whether <paramref name="file"/> is in a project's top-level <c>Disputes/</c> or <c>Transactions/</c> folder.</summary>
    private static bool IsInCustomerFeatureFolder(string file)
    {
        string[] parts = Path.GetRelativePath(SourceRoots.Src, file).Split(Path.DirectorySeparatorChar);
        return parts.Length > 2 && CustomerFeatureFolders.Contains(parts[1]);
    }

    private static string Describe((string File, int Number, string Text) line) =>
        $"{Path.GetRelativePath(SourceRoots.Src, line.File)}:{line.Number}: {line.Text.Trim()}";

    private static IEnumerable<(string File, int Number, string Text)> SourceLines() =>
        Directory.EnumerateFiles(SourceRoots.Src, "*.cs", SearchOption.AllDirectories)
            .Where(file => !SourceRoots.IsBuildOutput(file))
            .SelectMany(file => File.ReadLines(file).Select((text, index) => (file, index + 1, text)));

    [GeneratedRegex(@"\bIgnoreQueryFilters\s*\(")]
    private static partial Regex IgnoreQueryFilters();

    [GeneratedRegex(@"\bIgnoreQueryFilters\s*\(\s*\)")]
    private static partial Regex ParameterlessIgnoreQueryFilters();

    // An Operations or Sla namespace of a project, written in full or from the project name, and followed by "." or ";".
    // Not preceded by a word character or ".", so a project name inside another namespace (Vendor.Api.Operations,
    // PartnerApi.Operations) never matches. ActorType.Operations, the enum member, never matches because ActorType is not
    // a project name, and a bare "Sla" is the dispute's SLA constant, not a namespace.
    [GeneratedRegex(@"(?<![\w.])(?:DisputeDesk\.)?(?:Api|Application|Contracts|Domain|Infrastructure|Worker)\.(?:Operations|Sla)\b(?=\s*[.;])")]
    private static partial Regex OperationsOrSlaNamespace();
}
