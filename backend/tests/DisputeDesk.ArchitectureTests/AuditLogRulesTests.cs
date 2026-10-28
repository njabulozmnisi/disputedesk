using System.Text.RegularExpressions;

namespace DisputeDesk.ArchitectureTests;

/// <summary>
/// The audit log is append-only (AD-14; OWASP ASVS 4.0.3 V7.3.3). The commit override refuses a tracked audit entry
/// that is modified or deleted, but bulk and raw SQL writes never pass the change tracker, so no production file that
/// names the audit entries may call them. The database-level grant is on the production path.
/// </summary>
public partial class AuditLogRulesTests
{
    [Fact]
    public void ProductionSource_FileNamingAuditEntries_HasNoBulkOrRawSqlCall()
    {
        var violations = Directory.EnumerateFiles(SourceRoots.Src, "*.cs", SearchOption.AllDirectories)
            .Where(file => !SourceRoots.IsBuildOutput(file))
            .Select(file => (File: file, Text: File.ReadAllText(file)))
            .Where(source => NamesAuditEntries().IsMatch(source.Text) && BulkOrRawSqlCall().IsMatch(source.Text))
            .Select(source => Path.GetRelativePath(SourceRoots.Src, source.File))
            .ToList();

        Assert.True(violations.Count == 0, "Bulk or raw SQL call in a file naming the audit entries:\n" + string.Join('\n', violations));
    }

    [Theory]
    [InlineData("db.Set<AuditLogEntry>().ExecuteUpdateAsync(setters => setters.SetProperty(entry => entry.Result, value))", true)]
    [InlineData("query.ExecuteDelete()", true)]
    [InlineData("db.Database.ExecuteSqlAsync($\"DELETE FROM \\\"AuditLogEntries\\\"\")", true)]
    [InlineData("db.Database.ExecuteSqlRawAsync(sql)", true)]
    [InlineData("db.Set<AuditLogEntry>().FromSqlRaw(sql)", true)]
    [InlineData("db.Database.SqlQueryRaw<int>(sql)", true)]
    [InlineData("db.Database.SqlQuery<Guid>($\"DELETE FROM \\\"AuditLogEntries\\\" RETURNING \\\"Id\\\"\")", true)]
    [InlineData("Set<AuditLogEntry>().Add(entry);", false)]
    [InlineData("// ExecuteUpdate is not called here", false)]
    public void BulkOrRawSqlCall_SampleLine_IsMatchedOnlyWhenCalled(string line, bool expected)
    {
        Assert.Equal(expected, BulkOrRawSqlCall().IsMatch(line));
    }

    [Theory]
    [InlineData("Set<AuditLogEntry>()", true)]
    [InlineData("FROM \"AuditLogEntries\"", true)]
    [InlineData("Set<OutboxMessage>()", false)]
    public void NamesAuditEntries_SampleText_IsMatchedForTheTypeOrTheTable(string text, bool expected)
    {
        Assert.Equal(expected, NamesAuditEntries().IsMatch(text));
    }

    // The type, its configuration and the table: AuditLogEntry, AuditLogEntryConfiguration, AuditLogEntries.
    [GeneratedRegex(@"\bAuditLogEntr")]
    private static partial Regex NamesAuditEntries();

    // ExecuteUpdate(Async), ExecuteDelete(Async), ExecuteSql(Async/Interpolated/Raw), every *SqlRaw variant, and
    // SqlQuery(Raw), which runs any statement it is given, such as DELETE ... RETURNING.
    [GeneratedRegex(@"\b(?:ExecuteUpdate|ExecuteDelete|ExecuteSql|SqlQuery|\w*SqlRaw)\w*\s*[<(]")]
    private static partial Regex BulkOrRawSqlCall();
}
