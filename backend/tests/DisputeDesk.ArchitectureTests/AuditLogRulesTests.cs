using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DisputeDesk.ArchitectureTests;

/// <summary>
/// The audit log and the stored events are insert only (AD-14; OWASP ASVS 4.0.3 V7.3.3; OWASP Logging Cheat Sheet:
/// protect the integrity of event data). The commit override refuses a tracked audit entry or stored event that is
/// modified or deleted, but bulk and raw SQL writes never pass the change tracker, so no production file that names
/// either may call them. The database-level grant is on the production path.
/// </summary>
public class AuditLogRulesTests
{
    // The types, their configurations and the tables: AuditLogEntry, AuditLogEntryConfiguration, AuditLogEntries;
    // StoredEvent and StoredEventRow, StoredEventConfiguration, StoredEvents.
    private static readonly string[] InsertOnlyNames = ["AuditLogEntr", "StoredEvent"];

    // ExecuteUpdate(Async), ExecuteDelete(Async), ExecuteSql(Async/Interpolated/Raw) and SqlQuery(Raw), which runs any
    // statement it is given, such as DELETE ... RETURNING; every *SqlRaw method is matched separately.
    private static readonly string[] BulkOrRawSqlPrefixes = ["ExecuteUpdate", "ExecuteDelete", "ExecuteSql", "SqlQuery"];

    // Add, Attach and Update with their Async and Range forms.
    private static readonly string[] WritePrefixes = ["Add", "Attach", "Update"];

    [Fact]
    public void ProductionSource_FileNamingAuditEntriesOrStoredEvents_HasNoBulkOrRawSqlCall()
    {
        var violations = SourceFiles()
            .Where(NamesInsertOnlyRecords)
            .SelectMany(BulkOrRawSqlCalls)
            .Select(call => SourceTrees.Describe(call.GetLocation()))
            .ToList();

        Assert.True(violations.Count == 0, "Bulk or raw SQL call in a file naming the audit entries or stored events:\n" + string.Join('\n', violations));
    }

    [Fact]
    public void ProductionSource_BulkCall_IsFoundInTheSessionStore()
    {
        // Proves the scans read the bulk calls a file that names neither record may make, and the names in the audit log,
        // so an empty violation list is not a scan that found nothing.
        string sessions = Path.Combine(SourceRoots.Src, "DisputeDesk.Api", "Authentication", "SessionTicketStore.cs");

        Assert.NotEmpty(BulkOrRawSqlCalls(sessions));
        Assert.False(NamesInsertOnlyRecords(sessions));
        Assert.True(NamesInsertOnlyRecords(AuditLogFile));
    }

    [Fact]
    public void ProductionSource_FileOtherThanAuditLog_NeverConstructsOrAddsAnAuditEntry()
    {
        // AD-14a: AuditLog.Record is the one way to write an audit entry. The configuration, the context and the
        // migrations name the type for the model only.
        var violations = SourceFiles()
            .Where(file => !MayNameAuditEntries(file))
            .SelectMany(AuditEntryWrites)
            .Select(node => SourceTrees.Describe(node.GetLocation()))
            .ToList();

        Assert.True(violations.Count == 0, "An audit entry is written outside AuditLog.Record in:\n" + string.Join('\n', violations));
    }

    [Fact]
    public void ProductionSource_AuditEntryWrite_IsFoundInTheAuditLog()
    {
        // Proves the scan reads the write the rule allows, so an empty violation list is not a scan that found nothing.
        Assert.NotEmpty(AuditEntryWrites(AuditLogFile));
    }

    private static string AuditLogFile => Path.Combine(SourceRoots.Src, "DisputeDesk.Application", "Audit", "AuditLog.cs");

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(SourceRoots.Src, "*.cs", SearchOption.AllDirectories)
            .Where(file => !SourceRoots.IsBuildOutput(file));

    private static bool MayNameAuditEntries(string file) =>
        Path.GetFileName(file) is "AuditLog.cs" or "AuditLogEntryConfiguration.cs" or "AppDbContext.cs"
        || Path.GetRelativePath(SourceRoots.Src, file).Split(Path.DirectorySeparatorChar).Contains("Migrations");

    /// <summary>
    /// Whether <paramref name="file"/> names either record: an identifier that starts with one of the names, or a string
    /// (a literal or an interpolated string's text, as in raw SQL) that contains one.
    /// </summary>
    private static bool NamesInsertOnlyRecords(string file)
    {
        SyntaxNode root = SourceTrees.Root(file);
        bool namesAType = root.DescendantTokens()
            .Any(token => token.IsKind(SyntaxKind.IdentifierToken) && InsertOnlyNames.Any(name => token.ValueText.StartsWith(name, StringComparison.Ordinal)));
        bool namesATable = root.DescendantNodes()
            .Select(StringText)
            .Any(text => text is not null && InsertOnlyNames.Any(name => text.Contains(name, StringComparison.Ordinal)));
        return namesAType || namesATable;
    }

    private static string? StringText(SyntaxNode node) => node switch
    {
        LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression) => literal.Token.ValueText,
        InterpolatedStringTextSyntax text => text.TextToken.ValueText,
        _ => null,
    };

    private static IEnumerable<InvocationExpressionSyntax> BulkOrRawSqlCalls(string file) =>
        SourceTrees.Root(file).DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(call => SourceTrees.LastName(call.Expression) is { } name
                && (BulkOrRawSqlPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)) || name.Contains("SqlRaw", StringComparison.Ordinal)));

    /// <summary>
    /// Each write of an audit entry in <paramref name="file"/>: a <c>new AuditLogEntry</c>, a target-typed <c>new()</c>
    /// assigned to an <c>AuditLogEntry</c> variable, or an Add, Attach or Update on <c>Set&lt;AuditLogEntry&gt;()</c>.
    /// </summary>
    private static IEnumerable<SyntaxNode> AuditEntryWrites(string file) =>
        SourceTrees.Root(file).DescendantNodes().Where(node => node switch
        {
            ObjectCreationExpressionSyntax creation => SourceTrees.LastName(creation.Type) == "AuditLogEntry",
            ImplicitObjectCreationExpressionSyntax creation =>
                creation.Parent is EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax declaration } }
                && SourceTrees.LastName(declaration.Type) == "AuditLogEntry",
            InvocationExpressionSyntax call =>
                call.Expression is MemberAccessExpressionSyntax { Expression: InvocationExpressionSyntax set } write
                && WritePrefixes.Any(prefix => write.Name.Identifier.ValueText.StartsWith(prefix, StringComparison.Ordinal))
                && IsAuditEntrySet(set),
            _ => false,
        });

    /// <summary>Whether <paramref name="call"/> is a generic call with no arguments on the audit entry type, such as <c>Set&lt;AuditLogEntry&gt;()</c>.</summary>
    private static bool IsAuditEntrySet(InvocationExpressionSyntax call)
    {
        SyntaxNode method = call.Expression is MemberAccessExpressionSyntax access ? access.Name : call.Expression;
        return method is GenericNameSyntax generic
            && generic.TypeArgumentList.Arguments.Count == 1
            && SourceTrees.LastName(generic.TypeArgumentList.Arguments[0]) == "AuditLogEntry"
            && call.ArgumentList.Arguments.Count == 0;
    }
}
