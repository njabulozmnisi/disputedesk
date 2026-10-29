using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace DisputeDesk.Api.IntegrationTests.Disputes;

/// <summary>
/// AD-22: the migration history shows only forward development of the data model. The migrations drop no table and
/// create exactly the model's tables, the migration that creates <c>Disputes</c> also creates <c>StoredEvents</c>, and
/// each migration's designer model and the snapshot agree with the migrations and the model (EF Core, Managing migrations).
/// </summary>
public class MigrationHistoryTests(PostgresFixture postgres)
{
    private const string AddDisputes = "20261028084134_AddDisputes";

    [Fact]
    public async Task Migrations_UpOperations_DropNoTableAndCreateOnlyTheModelTables()
    {
        List<MigrationOperation> operations = [.. (await MigrationsAsync()).SelectMany(migration => migration.Migration.UpOperations)];
        await using AppDbContext db = postgres.CreateDbContext();
        IEnumerable<string> modelTables = db.GetService<IDesignTimeModel>().Model.GetRelationalModel().Tables.Select(table => table.Name);

        Assert.DoesNotContain(operations, operation => operation is DropTableOperation);
        Assert.Equal(
            modelTables.Order(StringComparer.Ordinal),
            operations.OfType<CreateTableOperation>().Select(table => table.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task AddDisputes_UpOperations_CreateDisputesAndStoredEventsWithTheUniqueVersion()
    {
        Migration addDisputes = (await MigrationsAsync()).Single(migration => migration.Id == AddDisputes).Migration;

        List<CreateTableOperation> tables = [.. addDisputes.UpOperations.OfType<CreateTableOperation>()];
        Assert.Contains(tables, table => table.Name == "Disputes");
        CreateTableOperation storedEvents = Assert.Single(tables, table => table.Name == "StoredEvents");
        AddForeignKeyOperation toDisputes = Assert.Single(storedEvents.ForeignKeys);
        Assert.Equal("Disputes", toDisputes.PrincipalTable);
        Assert.Equal(ReferentialAction.Restrict, toDisputes.OnDelete);
        CreateIndexOperation version = Assert.Single(addDisputes.UpOperations.OfType<CreateIndexOperation>(), index => index.Table == "StoredEvents");
        Assert.Equal("UQ_StoredEvents_DisputeId_Version", version.Name);
        Assert.True(version.IsUnique);
    }

    [Fact]
    public async Task Migrations_EachDesignerModel_ProducesExactlyThatMigrationsOperations()
    {
        // As the migrator does: finalise each designer's model, diff it against the one before, and compare the SQL the
        // differences generate with the SQL of the migration's own operations.
        await using AppDbContext db = postgres.CreateDbContext();
        IMigrationsModelDiffer differ = db.GetService<IMigrationsModelDiffer>();
        IMigrationsSqlGenerator sqlGenerator = db.GetService<IMigrationsSqlGenerator>();
        IRelationalModel? previous = null;

        foreach ((string id, Migration migration) in await MigrationsAsync())
        {
            IModel target = Finalise(db, migration.TargetModel ?? throw new InvalidOperationException($"{id} has no designer model."));
            IReadOnlyList<MigrationOperation> differences = differ.GetDifferences(previous, target.GetRelationalModel());

            Assert.Equal(Sql(sqlGenerator, migration.UpOperations, target), Sql(sqlGenerator, differences, target));
            previous = target.GetRelationalModel();
        }
    }

    [Fact]
    public async Task ModelSnapshot_MatchesTheModel()
    {
        await using AppDbContext db = postgres.CreateDbContext();
        ModelSnapshot snapshot = db.GetService<IMigrationsAssembly>().ModelSnapshot ?? throw new InvalidOperationException("No model snapshot.");

        bool differs = db.GetService<IMigrationsModelDiffer>().HasDifferences(
            Finalise(db, snapshot.Model).GetRelationalModel(), db.GetService<IDesignTimeModel>().Model.GetRelationalModel());

        Assert.False(differs);
    }

    private async Task<List<(string Id, Migration Migration)>> MigrationsAsync()
    {
        await using AppDbContext db = postgres.CreateDbContext();
        IMigrationsAssembly migrations = db.GetService<IMigrationsAssembly>();
        string provider = db.Database.ProviderName ?? throw new InvalidOperationException("No provider.");
        return [.. migrations.Migrations.Select(migration => (migration.Key, migrations.CreateMigration(migration.Value, provider)))];
    }

    private static IModel Finalise(AppDbContext db, IModel model)
    {
        IModel finalised = model is IMutableModel mutable ? mutable.FinalizeModel() : model;
        return db.GetService<IModelRuntimeInitializer>().Initialize(finalised);
    }

    // A migration lists a new table's columns in the order the model built them, while a designer model reads them back
    // in name order, so each statement is compared as its set of lines: any other difference still shows.
    private static List<string> Sql(IMigrationsSqlGenerator generator, IReadOnlyList<MigrationOperation> operations, IModel model) =>
        [.. generator.Generate(operations, model).Select(command => LinesAsASet(command.CommandText))];

    private static string LinesAsASet(string statement) =>
        string.Join('\n', statement.Split('\n').Select(line => line.Trim().TrimEnd(',')).Order(StringComparer.Ordinal));
}
