using System;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DisputeDesk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLinkingChallengesAndAuditClientIp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<IPAddress>(
                name: "ClientIp",
                table: "AuditLogEntries",
                type: "inet",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LinkingChallenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LoginSub = table.Column<string>(type: "text", nullable: false),
                    CustomerId = table.Column<string>(type: "text", nullable: true),
                    OtpHmac = table.Column<byte[]>(type: "bytea", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OtpSentAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SupersededAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConsumedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LinkingChallenges", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "UQ_LinkingChallenges_LoginSub",
                table: "LinkingChallenges",
                column: "LoginSub",
                unique: true,
                filter: "\"SupersededAtUtc\" IS NULL AND \"ConsumedAtUtc\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LinkingChallenges");

            migrationBuilder.DropColumn(
                name: "ClientIp",
                table: "AuditLogEntries");
        }
    }
}
