using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RiskGuard.Modules.Ingestion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialIngestion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ingestion");

            migrationBuilder.CreateTable(
                name: "Orders",
                schema: "ingestion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Platform = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ExternalOrderId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CustomerEmail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RawWebhooks",
                schema: "ingestion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Platform = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Headers = table.Column<string>(type: "jsonb", nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RawWebhooks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_TenantId_CustomerEmail",
                schema: "ingestion",
                table: "Orders",
                columns: new[] { "TenantId", "CustomerEmail" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_TenantId_Platform_ExternalOrderId",
                schema: "ingestion",
                table: "Orders",
                columns: new[] { "TenantId", "Platform", "ExternalOrderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RawWebhooks_TenantId_ReceivedAt",
                schema: "ingestion",
                table: "RawWebhooks",
                columns: new[] { "TenantId", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RawWebhooks_TenantId_Status",
                schema: "ingestion",
                table: "RawWebhooks",
                columns: new[] { "TenantId", "Status" });

            // Row-Level Security (mesmo modelo do schema tenant): sem app.current_tenant => nenhuma linha.
            migrationBuilder.Sql("""
                ALTER TABLE ingestion."RawWebhooks" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE ingestion."Orders" ENABLE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation ON ingestion."RawWebhooks"
                    USING ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid)
                    WITH CHECK ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid);

                CREATE POLICY tenant_isolation ON ingestion."Orders"
                    USING ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid)
                    WITH CHECK ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid);
                """);

            // Sem DELETE: webhooks e pedidos sao trilha de auditoria.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'riskguard_app') THEN
                        GRANT USAGE ON SCHEMA ingestion TO riskguard_app;
                        GRANT SELECT, INSERT, UPDATE ON ingestion."RawWebhooks" TO riskguard_app;
                        GRANT SELECT, INSERT, UPDATE ON ingestion."Orders" TO riskguard_app;
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP POLICY IF EXISTS tenant_isolation ON ingestion."Orders";""");
            migrationBuilder.Sql("""DROP POLICY IF EXISTS tenant_isolation ON ingestion."RawWebhooks";""");

            migrationBuilder.DropTable(
                name: "Orders",
                schema: "ingestion");

            migrationBuilder.DropTable(
                name: "RawWebhooks",
                schema: "ingestion");
        }
    }
}
