using System.Reflection;
using System.Text.RegularExpressions;
using DisputeDesk.Application.Abstractions;
using DisputeDesk.Infrastructure.Persistence;

namespace DisputeDesk.ArchitectureTests;

/// <summary>
/// AC9, AD-11: the <c>Ownership</c> filter is bypassed only in <c>DisputeDesk.Application/Operations/</c> and
/// <c>DisputeDesk.Worker/Sla/</c>, no context is pooled (a pooled context would keep one request's customer for the
/// next), and the filter reads a nullable value on the context instead of <c>CurrentCustomer.CustomerId</c>, which
/// throws when unset.
/// </summary>
public partial class OwnershipRulesTests
{
    // AD-11 names exactly these two folders, each in one project.
    private static readonly string[] BypassFolders =
    [
        Path.Combine("DisputeDesk.Application", "Operations") + Path.DirectorySeparatorChar,
        Path.Combine("DisputeDesk.Worker", "Sla") + Path.DirectorySeparatorChar,
    ];

    [Fact]
    public void ProductionSource_IgnoreQueryFilters_AppearsOnlyInOperationsAndSla()
    {
        var violations = SourceLines()
            .Where(line => IgnoreQueryFilters().IsMatch(line.Text) && !IsInBypassFolder(line.File))
            .Select(line => $"{Path.GetRelativePath(SourceRoots.Src, line.File)}:{line.Number}: {line.Text.Trim()}")
            .ToList();

        Assert.True(violations.Count == 0, "IgnoreQueryFilters outside Operations/ and Sla/:\n" + string.Join('\n', violations));
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

    private static IEnumerable<(string File, int Number, string Text)> SourceLines() =>
        Directory.EnumerateFiles(SourceRoots.Src, "*.cs", SearchOption.AllDirectories)
            .Where(file => !SourceRoots.IsBuildOutput(file))
            .SelectMany(file => File.ReadLines(file).Select((text, index) => (file, index + 1, text)));

    [GeneratedRegex(@"\bIgnoreQueryFilters\s*\(")]
    private static partial Regex IgnoreQueryFilters();
}
