using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RiskGuard.Modules.Tenant.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialTenant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "tenant");

            migrationBuilder.CreateTable(
                name: "Tenants",
                schema: "tenant",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TenantSettings",
                schema: "tenant",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    HighRiskThreshold = table.Column<int>(type: "integer", nullable: false),
                    AutoBlockEnabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantSettings", x => x.TenantId);
                    table.ForeignKey(
                        name: "FK_TenantSettings_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "tenant",
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Email",
                schema: "tenant",
                table: "Tenants",
                column: "Email",
                unique: true);

            // Row-Level Security: o banco recusa linhas de outro tenant mesmo que o filtro do EF seja contornado.
            // A policy le a variavel de sessao app.current_tenant (gravada pelo TenantSessionInterceptor);
            // sem valor => NULL => nenhuma linha (fail-closed). ENABLE (sem FORCE): o dono do schema
            // (migrations/administracao) nao e filtrado; o papel restrito da aplicacao e.
            migrationBuilder.Sql("""
                ALTER TABLE tenant."TenantSettings" ENABLE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation ON tenant."TenantSettings"
                    USING ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid)
                    WITH CHECK ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid);
                """);

            // Privilegios minimos para o papel da aplicacao (se existir; criado pelo provisionamento do banco).
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'riskguard_app') THEN
                        GRANT USAGE ON SCHEMA tenant TO riskguard_app;
                        GRANT SELECT, INSERT, UPDATE ON tenant."Tenants" TO riskguard_app;
                        GRANT SELECT, INSERT, UPDATE ON tenant."TenantSettings" TO riskguard_app;
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP POLICY IF EXISTS tenant_isolation ON tenant."TenantSettings";""");

            migrationBuilder.DropTable(
                name: "TenantSettings",
                schema: "tenant");

            migrationBuilder.DropTable(
                name: "Tenants",
                schema: "tenant");
        }
    }
}
