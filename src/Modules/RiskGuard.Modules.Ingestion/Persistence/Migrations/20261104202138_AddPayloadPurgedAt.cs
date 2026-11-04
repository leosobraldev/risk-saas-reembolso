using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RiskGuard.Modules.Ingestion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayloadPurgedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PayloadPurgedAt",
                schema: "ingestion",
                table: "RawWebhooks",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PayloadPurgedAt",
                schema: "ingestion",
                table: "RawWebhooks");
        }
    }
}
