using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RiskGuard.Modules.Tenant.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExternalIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PasswordHash",
                schema: "tenant",
                table: "Tenants");

            migrationBuilder.CreateTable(
                name: "TenantUsers",
                schema: "tenant",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantUsers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TenantUsers_ExternalUserId",
                schema: "tenant",
                table: "TenantUsers",
                column: "ExternalUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantUsers_TenantId",
                schema: "tenant",
                table: "TenantUsers",
                column: "TenantId");

            // Row-Level Security: o vinculo usuario<->lojista so e visivel ao proprio lojista (sem app.current_tenant => nenhuma linha).
            migrationBuilder.Sql("""
                ALTER TABLE tenant."TenantUsers" ENABLE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation ON tenant."TenantUsers"
                    USING ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid)
                    WITH CHECK ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid);
                """);

            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'riskguard_app') THEN
                        GRANT SELECT, INSERT ON tenant."TenantUsers" TO riskguard_app;
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP POLICY IF EXISTS tenant_isolation ON tenant."TenantUsers";""");
            migrationBuilder.DropTable(
                name: "TenantUsers",
                schema: "tenant");

            migrationBuilder.AddColumn<string>(
                name: "PasswordHash",
                schema: "tenant",
                table: "Tenants",
                type: "text",
                nullable: false,
                defaultValue: "");
        }
    }
}
