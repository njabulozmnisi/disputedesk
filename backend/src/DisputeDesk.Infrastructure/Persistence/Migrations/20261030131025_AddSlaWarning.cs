using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DisputeDesk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSlaWarning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SlaWarningRaisedAtUtc",
                table: "Disputes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Disputes_SlaWarning",
                table: "Disputes",
                column: "SlaDueAtUtc",
                filter: "\"SlaWarningRaisedAtUtc\" IS NULL AND \"Status\" IN ('Submitted', 'UnderReview')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Disputes_SlaWarning",
                table: "Disputes");

            migrationBuilder.DropColumn(
                name: "SlaWarningRaisedAtUtc",
                table: "Disputes");
        }
    }
}
