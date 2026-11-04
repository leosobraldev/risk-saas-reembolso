using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RiskGuard.Modules.Action.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "action");

            migrationBuilder.CreateTable(
                name: "OrderActions",
                schema: "action",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvaluationId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Platform = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ExternalOrderId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderActions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderActions_EvaluationId",
                schema: "action",
                table: "OrderActions",
                column: "EvaluationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderActions_TenantId_ExternalOrderId",
                schema: "action",
                table: "OrderActions",
                columns: new[] { "TenantId", "ExternalOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderActions_TenantId_Status",
                schema: "action",
                table: "OrderActions",
                columns: new[] { "TenantId", "Status" });

            // Row-Level Security (mesmo modelo dos demais schemas): sem app.current_tenant => nenhuma linha.
            migrationBuilder.Sql("""
                ALTER TABLE action."OrderActions" ENABLE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation ON action."OrderActions"
                    USING ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid)
                    WITH CHECK ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid);
                """);

            // Trilha de auditoria: sem DELETE para o papel da aplicacao.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'riskguard_app') THEN
                        GRANT USAGE ON SCHEMA action TO riskguard_app;
                        GRANT SELECT, INSERT, UPDATE ON action."OrderActions" TO riskguard_app;
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP POLICY IF EXISTS tenant_isolation ON action."OrderActions";""");
            migrationBuilder.DropTable(
                name: "OrderActions",
                schema: "action");
        }
    }
}
