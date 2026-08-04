using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Almacen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AlmacenSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "settings",
                schema: "almacen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reabasto_automatico_activo = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
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
                name: "ux_almacen_settings_empresa_id",
                schema: "almacen",
                table: "settings",
                column: "empresa_id",
                unique: true);

            // Seed: una fila por cada empresa existente con default false (el
            // motor de reabasto arranca apagado; se enciende desde la pantalla
            // de Reabasto). Si una empresa se aprovisiona después, el handler
            // de upsert crea la fila on-demand al escribir. Molde de la
            // migración ComprasSettings.
            migrationBuilder.Sql("""
                INSERT INTO almacen.settings (
                    id, empresa_id, reabasto_automatico_activo,
                    version, created_at, updated_at, created_by, updated_by, deleted_at)
                SELECT gen_random_uuid(), e.id, false,
                       0, now() AT TIME ZONE 'UTC', now() AT TIME ZONE 'UTC',
                       'migration-AlmacenSettings', 'migration-AlmacenSettings', NULL
                FROM compartido.empresas e
                ON CONFLICT (empresa_id) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "settings",
                schema: "almacen");
        }
    }
}
