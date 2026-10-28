using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DisputeDesk.Api.IntegrationTests.Disputes;

/// <summary>
/// AC8: the migrated schema, read back from Postgres's catalogue: money, instants, the row version, the indexes and
/// the outbox columns follow AD-4, AD-15, AD-17 and AD-22.
/// </summary>
public class DisputeSchemaTests(PostgresFixture postgres)
{
    private static readonly string[] DisputeTables = ["Disputes", "TimelineEntries", "AuditLogEntries", "OutboxMessages"];

    [Theory]
    [InlineData("TransactionAmount")]
    [InlineData("ExpectedAmount")]
    [InlineData("DisputedAmount")]
    public async Task Disputes_MoneyColumn_IsNumeric19Comma2(string column)
    {
        Column money = Assert.Single(await ColumnsAsync(), candidate => candidate.Table == "Disputes" && candidate.Name == column);

        Assert.Equal("numeric", money.Type);
        Assert.Equal(19, money.Precision);
        Assert.Equal(2, money.Scale);
    }

    [Fact]
    public async Task Disputes_Currency_IsAThreeCharacterCode()
    {
        Column currency = Assert.Single(await ColumnsAsync(), candidate => candidate.Table == "Disputes" && candidate.Name == "Currency");

        // varchar(3), not character(3), which pads with spaces (PostgreSQL wiki, "Don't Do This": char(n)).
        Assert.Equal("character varying", currency.Type);
        Assert.Equal(3, currency.Length);
        Assert.Equal("NO", currency.Nullable);
    }

    [Fact]
    public async Task DisputeTables_EveryInstant_IsTimestamptzAndEndsInUtc()
    {
        List<Column> columns = [.. (await ColumnsAsync()).Where(column => DisputeTables.Contains(column.Table))];

        IEnumerable<Column> instants = columns.Where(column => column.Type.StartsWith("timestamp", StringComparison.Ordinal));
        Assert.NotEmpty(instants);
        Assert.All(instants, column => Assert.Equal("timestamp with time zone", column.Type));
        Assert.All(instants, column => Assert.EndsWith("Utc", column.Name, StringComparison.Ordinal));
        Assert.All(columns.Where(column => column.Name.EndsWith("Utc", StringComparison.Ordinal)),
            column => Assert.Equal("timestamp with time zone", column.Type));
    }

    [Fact]
    public async Task DisputeTables_NoColumn_HasADefault()
    {
        // AD-4: no time defaults; every value comes from the use case.
        Assert.All((await ColumnsAsync()).Where(column => DisputeTables.Contains(column.Table)), column => Assert.Null(column.Default));
    }

    [Fact]
    public async Task Disputes_RowVersion_IsTheXminConcurrencyTokenAndVersionIsAnInteger()
    {
        await using AppDbContext db = postgres.CreateDbContext();
        IEntityType dispute = db.Model.FindEntityType(typeof(Dispute)) ?? throw new InvalidOperationException("Dispute is not mapped.");

        IProperty rowVersion = dispute.FindProperty("RowVersion") ?? throw new InvalidOperationException("No row version.");
        Assert.Equal(typeof(uint), rowVersion.ClrType);
        Assert.True(rowVersion.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, rowVersion.ValueGenerated);
        Assert.Equal("xmin", rowVersion.GetColumnName());
        Column version = Assert.Single(await ColumnsAsync(), column => column.Table == "Disputes" && column.Name == "Version");
        Assert.Equal("integer", version.Type);
        Assert.DoesNotContain(await ColumnsAsync(), column => column.Table == "Disputes" && column.Name is "xmin" or "RowVersion");
    }

    [Fact]
    public async Task Disputes_Indexes_AreTheUniqueTransactionIndexAndThePartialSlaIndex()
    {
        await using AppDbContext db = postgres.CreateDbContext();

        List<string> definitions = await db.Database
            .SqlQuery<string>($"SELECT indexdef AS \"Value\" FROM pg_indexes WHERE tablename = 'Disputes' ORDER BY indexname")
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                "CREATE INDEX \"IX_Disputes_SlaDueAtUtc\" ON public.\"Disputes\" USING btree (\"SlaDueAtUtc\") WHERE (\"Status\" = ANY (ARRAY['Submitted'::text, 'UnderReview'::text]))",
                "CREATE UNIQUE INDEX \"PK_Disputes\" ON public.\"Disputes\" USING btree (\"Id\")",
                "CREATE UNIQUE INDEX \"UQ_Disputes_AccountRef_TransactionId\" ON public.\"Disputes\" USING btree (\"AccountRef\", \"TransactionId\")",
            ],
            definitions);
    }

    [Fact]
    public async Task DisputeTables_ForeignKeys_FollowTheAd22Names()
    {
        await using AppDbContext db = postgres.CreateDbContext();

        List<string> foreignKeys = await db.Database
            .SqlQuery<string>($"SELECT conname AS \"Value\" FROM pg_constraint WHERE contype = 'f' AND conrelid::regclass::text IN ('\"TimelineEntries\"', '\"OutboxMessages\"', '\"AuditLogEntries\"') ORDER BY conname")
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["FK_OutboxMessages_Disputes_DisputeId", "FK_TimelineEntries_Disputes_DisputeId"], foreignKeys);
    }

    [Fact]
    public async Task OutboxMessages_Columns_HoldEverythingTheRelayNeeds()
    {
        List<Column> outbox = [.. (await ColumnsAsync()).Where(column => column.Table == "OutboxMessages")];

        Assert.Equal(
            ["Attempts", "CreatedAtUtc", "DisputeId", "DisputeVersion", "EventType", "Id", "LastError", "NextAttemptAtUtc", "Payload", "SentAtUtc", "Status", "TraceParent", "TraceState"],
            outbox.Select(column => column.Name).Order(StringComparer.Ordinal));
        Assert.Equal("text", outbox.Single(column => column.Name == "Payload").Type);
        Assert.Equal("YES", outbox.Single(column => column.Name == "TraceParent").Nullable);
    }

    private async Task<List<Column>> ColumnsAsync()
    {
        await using AppDbContext db = postgres.CreateDbContext();
        return await db.Database.SqlQuery<Column>($"""
            SELECT table_name AS "Table", column_name AS "Name", data_type AS "Type", numeric_precision AS "Precision",
                   numeric_scale AS "Scale", character_maximum_length AS "Length", is_nullable AS "Nullable", column_default AS "Default"
            FROM information_schema.columns WHERE table_schema = 'public'
            """).ToListAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>One row of <c>information_schema.columns</c>.</summary>
    private sealed record Column(string Table, string Name, string Type, int? Precision, int? Scale, int? Length, string Nullable, string? Default);
}
