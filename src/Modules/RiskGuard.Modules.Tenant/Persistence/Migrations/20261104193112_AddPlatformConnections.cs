using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RiskGuard.Modules.Tenant.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlatformConnections",
                schema: "tenant",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Platform = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    AccountId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    WebhookSecretProtected = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    AccessTokenProtected = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformConnections", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformConnections_TenantId_Platform",
                schema: "tenant",
                table: "PlatformConnections",
                columns: new[] { "TenantId", "Platform" },
                unique: true);

            // Row-Level Security: segredos cifrados so sao visiveis ao proprio tenant (sem app.current_tenant => nenhuma linha).
            migrationBuilder.Sql("""
                ALTER TABLE tenant."PlatformConnections" ENABLE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation ON tenant."PlatformConnections"
                    USING ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid)
                    WITH CHECK ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid);
                """);

            // Desconectar uma plataforma remove a linha (unica tabela do schema com DELETE para o papel da aplicacao).
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'riskguard_app') THEN
                        GRANT SELECT, INSERT, UPDATE, DELETE ON tenant."PlatformConnections" TO riskguard_app;
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP POLICY IF EXISTS tenant_isolation ON tenant."PlatformConnections";""");
            migrationBuilder.DropTable(
                name: "PlatformConnections",
                schema: "tenant");
        }
    }
}
