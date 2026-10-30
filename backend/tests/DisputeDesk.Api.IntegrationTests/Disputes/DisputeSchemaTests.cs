using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DisputeDesk.Api.IntegrationTests.Disputes;

/// <summary>
/// The migrated schema, read back from Postgres's catalogue: money, instants, the stored events, the indexes and the
/// outbox columns follow AD-4, AD-15, AD-17 and AD-22, and <c>Disputes</c> has no concurrency token (ad-009).
/// </summary>
public class DisputeSchemaTests(PostgresFixture postgres)
{
    private static readonly string[] DisputeTables = ["Disputes", "StoredEvents", "AuditLogEntries", "OutboxMessages"];

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
    public async Task Disputes_Model_HasNoConcurrencyTokenAndVersionIsAnInteger()
    {
        // AD-15: UQ_StoredEvents_DisputeId_Version is the one concurrency check, so Disputes maps no row version.
        await using AppDbContext db = postgres.CreateDbContext();
        IEntityType dispute = db.Model.FindEntityType(typeof(Dispute)) ?? throw new InvalidOperationException("Dispute is not mapped.");

        Assert.DoesNotContain(dispute.GetProperties(), property => property.IsConcurrencyToken);
        Assert.DoesNotContain(dispute.GetProperties(), property => property.GetColumnName() is "xmin" or "RowVersion");
        Column version = Assert.Single(await ColumnsAsync(), column => column.Table == "Disputes" && column.Name == "Version");
        Assert.Equal("integer", version.Type);
    }

    [Fact]
    public async Task StoredEvents_Columns_AreTheAd22Columns()
    {
        List<Column> storedEvents = [.. (await ColumnsAsync()).Where(column => column.Table == "StoredEvents")];

        Assert.Equal(
            [
                ("ActorId", "text", "NO"), ("ActorType", "text", "NO"), ("Data", "jsonb", "NO"), ("DisputeId", "uuid", "NO"),
                ("EventType", "text", "NO"), ("Id", "uuid", "NO"), ("OccurredAtUtc", "timestamp with time zone", "NO"),
                ("TraceId", "text", "YES"), ("Version", "integer", "NO"),
            ],
            storedEvents.Select(column => (column.Name, column.Type, column.Nullable)).OrderBy(column => column.Name, StringComparer.Ordinal));
    }

    [Fact]
    public async Task StoredEvents_Indexes_AreOnlyTheKeyAndTheUniqueVersion()
    {
        await using AppDbContext db = postgres.CreateDbContext();

        List<string> definitions = await db.Database
            .SqlQuery<string>($"SELECT indexdef AS \"Value\" FROM pg_indexes WHERE tablename = 'StoredEvents' ORDER BY indexname")
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                "CREATE UNIQUE INDEX \"PK_StoredEvents\" ON public.\"StoredEvents\" USING btree (\"Id\")",
                "CREATE UNIQUE INDEX \"UQ_StoredEvents_DisputeId_Version\" ON public.\"StoredEvents\" USING btree (\"DisputeId\", \"Version\")",
            ],
            definitions);
    }

    [Fact]
    public async Task StoredEvents_ForeignKeyToDisputes_RestrictsDelete()
    {
        await using AppDbContext db = postgres.CreateDbContext();

        // confdeltype 'r' is ON DELETE RESTRICT (PostgreSQL docs, pg_constraint).
        List<string> foreignKeys = await db.Database
            .SqlQuery<string>($"SELECT conname || ' ' || confdeltype::text AS \"Value\" FROM pg_constraint WHERE contype = 'f' AND conrelid::regclass::text = '\"StoredEvents\"'")
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["FK_StoredEvents_Disputes_DisputeId r"], foreignKeys);
    }

    [Fact]
    public async Task Disputes_Indexes_AreTheUniqueTransactionIndexAndThePartialSlaAndSlaWarningIndexes()
    {
        // Story 5.3 adds IX_Disputes_SlaWarning, the warning claim's partial index (AD-18).
        await using AppDbContext db = postgres.CreateDbContext();

        List<string> definitions = await db.Database
            .SqlQuery<string>($"SELECT indexdef AS \"Value\" FROM pg_indexes WHERE tablename = 'Disputes' ORDER BY indexname")
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                "CREATE INDEX \"IX_Disputes_SlaDueAtUtc\" ON public.\"Disputes\" USING btree (\"SlaDueAtUtc\") WHERE (\"Status\" = ANY (ARRAY['Submitted'::text, 'UnderReview'::text]))",
                "CREATE INDEX \"IX_Disputes_SlaWarning\" ON public.\"Disputes\" USING btree (\"SlaDueAtUtc\") WHERE ((\"SlaWarningRaisedAtUtc\" IS NULL) AND (\"Status\" = ANY (ARRAY['Submitted'::text, 'UnderReview'::text])))",
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
            .SqlQuery<string>($"SELECT conname AS \"Value\" FROM pg_constraint WHERE contype = 'f' AND conrelid::regclass::text IN ('\"StoredEvents\"', '\"OutboxMessages\"', '\"AuditLogEntries\"') ORDER BY conname")
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["FK_OutboxMessages_Disputes_DisputeId", "FK_StoredEvents_Disputes_DisputeId"], foreignKeys);
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
