using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RiskGuard.Modules.Risk.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialRisk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "risk");

            migrationBuilder.CreateTable(
                name: "CustomerHistories",
                schema: "risk",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerEmail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    TotalRefunds = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerHistories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RiskEvaluations",
                schema: "risk",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Platform = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ExternalOrderId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    Level = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    CalculatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiskEvaluations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RiskFactors",
                schema: "risk",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RiskEvaluationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    PointsAdded = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiskFactors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RiskFactors_RiskEvaluations_RiskEvaluationId",
                        column: x => x.RiskEvaluationId,
                        principalSchema: "risk",
                        principalTable: "RiskEvaluations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerHistories_TenantId_CustomerEmail",
                schema: "risk",
                table: "CustomerHistories",
                columns: new[] { "TenantId", "CustomerEmail" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RiskEvaluations_OrderId",
                schema: "risk",
                table: "RiskEvaluations",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RiskEvaluations_TenantId_CalculatedAt",
                schema: "risk",
                table: "RiskEvaluations",
                columns: new[] { "TenantId", "CalculatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RiskEvaluations_TenantId_Level",
                schema: "risk",
                table: "RiskEvaluations",
                columns: new[] { "TenantId", "Level" });

            migrationBuilder.CreateIndex(
                name: "IX_RiskFactors_RiskEvaluationId",
                schema: "risk",
                table: "RiskFactors",
                column: "RiskEvaluationId");

            // Row-Level Security (mesmo modelo dos demais schemas): sem app.current_tenant => nenhuma linha.
            // RiskFactors carrega TenantId proprio justamente para ter policy direta.
            migrationBuilder.Sql("""
                ALTER TABLE risk."RiskEvaluations" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE risk."RiskFactors" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE risk."CustomerHistories" ENABLE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation ON risk."RiskEvaluations"
                    USING ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid)
                    WITH CHECK ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid);

                CREATE POLICY tenant_isolation ON risk."RiskFactors"
                    USING ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid)
                    WITH CHECK ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid);

                CREATE POLICY tenant_isolation ON risk."CustomerHistories"
                    USING ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid)
                    WITH CHECK ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid);
                """);

            // Avaliacoes e fatores sao imutaveis (sem UPDATE/DELETE); o historico do cliente e atualizavel.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'riskguard_app') THEN
                        GRANT USAGE ON SCHEMA risk TO riskguard_app;
                        GRANT SELECT, INSERT ON risk."RiskEvaluations" TO riskguard_app;
                        GRANT SELECT, INSERT ON risk."RiskFactors" TO riskguard_app;
                        GRANT SELECT, INSERT, UPDATE ON risk."CustomerHistories" TO riskguard_app;
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP POLICY IF EXISTS tenant_isolation ON risk."CustomerHistories";""");
            migrationBuilder.Sql("""DROP POLICY IF EXISTS tenant_isolation ON risk."RiskFactors";""");
            migrationBuilder.Sql("""DROP POLICY IF EXISTS tenant_isolation ON risk."RiskEvaluations";""");

            migrationBuilder.DropTable(
                name: "CustomerHistories",
                schema: "risk");

            migrationBuilder.DropTable(
                name: "RiskFactors",
                schema: "risk");

            migrationBuilder.DropTable(
                name: "RiskEvaluations",
                schema: "risk");
        }
    }
}
