using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DisputeDesk.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddCustomerLinks : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "CustomerLinks",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                LoginSub = table.Column<string>(type: "text", nullable: false),
                CustomerId = table.Column<string>(type: "text", nullable: false),
                LinkedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CustomerLinks", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "UQ_CustomerLinks_CustomerId",
            table: "CustomerLinks",
            column: "CustomerId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "UQ_CustomerLinks_LoginSub",
            table: "CustomerLinks",
            column: "LoginSub",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "CustomerLinks");
    }
}
