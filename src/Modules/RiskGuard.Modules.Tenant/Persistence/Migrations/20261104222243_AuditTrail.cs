using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RiskGuard.Modules.Tenant.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditTrail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditEvents",
                schema: "tenant",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActorId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Target = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Details = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_TenantId_OccurredAt",
                schema: "tenant",
                table: "AuditEvents",
                columns: new[] { "TenantId", "OccurredAt" });

            // Trilha imutavel + isolamento: uma policy por comando. Nao existe policy (nem GRANT) de UPDATE: ninguem reescreve a historia.
            // DELETE so enxerga eventos com mais de 180 dias (minimo de guarda imposto pelo banco, igual ao minimo de Retention:AuditDays).
            migrationBuilder.Sql("""
                ALTER TABLE tenant."AuditEvents" ENABLE ROW LEVEL SECURITY;

                CREATE POLICY audit_select ON tenant."AuditEvents" FOR SELECT
                    USING ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid);

                CREATE POLICY audit_insert ON tenant."AuditEvents" FOR INSERT
                    WITH CHECK ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid);

                CREATE POLICY audit_retention_delete ON tenant."AuditEvents" FOR DELETE
                    USING ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                           AND "OccurredAt" < now() - interval '180 days');
                """);

            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'riskguard_app') THEN
                        GRANT SELECT, INSERT, DELETE ON tenant."AuditEvents" TO riskguard_app;
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP POLICY IF EXISTS audit_select ON tenant."AuditEvents";
                DROP POLICY IF EXISTS audit_insert ON tenant."AuditEvents";
                DROP POLICY IF EXISTS audit_retention_delete ON tenant."AuditEvents";
                """);
            migrationBuilder.DropTable(
                name: "AuditEvents",
                schema: "tenant");
        }
    }
}
