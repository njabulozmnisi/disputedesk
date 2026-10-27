using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DisputeDesk.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddSessionsAndDataProtectionKeys : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "DataProtectionKeys",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                FriendlyName = table.Column<string>(type: "text", nullable: true),
                Xml = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DataProtectionKeys", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Sessions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                KeyHash = table.Column<byte[]>(type: "bytea", nullable: false),
                LoginSub = table.Column<string>(type: "text", nullable: false),
                Ticket = table.Column<byte[]>(type: "bytea", nullable: false),
                ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                AbsoluteExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Sessions", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "UQ_Sessions_KeyHash",
            table: "Sessions",
            column: "KeyHash",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "DataProtectionKeys");

        migrationBuilder.DropTable(
            name: "Sessions");
    }
}
