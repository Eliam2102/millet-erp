using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ComprasSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "settings",
                schema: "compras",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    auto_generar_oc_al_autorizar = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_settings", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_settings_empresa_id",
                schema: "compras",
                table: "settings",
                column: "empresa_id",
                unique: true);

            // Seed: una fila por cada empresa existente con default false
            // (narrativa manual; el comprador convierte RQ→OC). Si la empresa
            // se aprovisionó después de esta migración, el handler de upsert
            // crea la fila on-demand al leerla por primera vez.
            //
            // Usa gen_random_uuid() (extensión pgcrypto, default en Postgres
            // 13+) para los ids. created_at/updated_at en now() UTC.
            migrationBuilder.Sql("""
                INSERT INTO compras.settings (
                    id, empresa_id, auto_generar_oc_al_autorizar,
                    version, created_at, updated_at, created_by, updated_by, deleted_at)
                SELECT gen_random_uuid(), e.id, false,
                       0, now() AT TIME ZONE 'UTC', now() AT TIME ZONE 'UTC',
                       'migration-ComprasSettings', 'migration-ComprasSettings', NULL
                FROM compartido.empresas e
                ON CONFLICT (empresa_id) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "settings",
                schema: "compras");
        }
    }
}
