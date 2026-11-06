using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.ActivosFijos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P24BrechasAuditoriaActivos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "motivo_cambio",
                schema: "activos_fijos",
                table: "versiones_clase",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "motivo_retiro",
                schema: "activos_fijos",
                table: "operaciones_libro",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "retirado_en",
                schema: "activos_fijos",
                table: "operaciones_libro",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "retirado_por",
                schema: "activos_fijos",
                table: "operaciones_libro",
                type: "uuid",
                nullable: true);

            // Conserva la solicitud más reciente y deja evidencia del retiro de duplicadas históricas.
            migrationBuilder.Sql("""
                WITH pendientes AS (
                    SELECT id, row_number() OVER (PARTITION BY empresa_id, activo_id ORDER BY created_at DESC, id DESC) AS posicion
                    FROM activos_fijos.operaciones_libro
                    WHERE tipo = 'Baja' AND poliza_id IS NULL AND NOT anulada AND rechazado_en IS NULL
                )
                UPDATE activos_fijos.operaciones_libro AS o
                SET anulada = TRUE, motivo_retiro = 'Duplicada al crear el candado (E-3)',
                    retirado_en = CURRENT_TIMESTAMP, updated_at = CURRENT_TIMESTAMP, version = o.version + 1
                FROM pendientes AS p WHERE o.id = p.id AND p.posicion > 1;
                """);

            migrationBuilder.CreateIndex(
                name: "ux_operacion_baja_pendiente",
                schema: "activos_fijos",
                table: "operaciones_libro",
                columns: new[] { "empresa_id", "activo_id" },
                unique: true,
                filter: "tipo = 'Baja' AND poliza_id IS NULL AND NOT anulada AND rechazado_en IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_operacion_baja_pendiente",
                schema: "activos_fijos",
                table: "operaciones_libro");

            migrationBuilder.DropColumn(
                name: "motivo_cambio",
                schema: "activos_fijos",
                table: "versiones_clase");

            migrationBuilder.DropColumn(
                name: "motivo_retiro",
                schema: "activos_fijos",
                table: "operaciones_libro");

            migrationBuilder.DropColumn(
                name: "retirado_en",
                schema: "activos_fijos",
                table: "operaciones_libro");

            migrationBuilder.DropColumn(
                name: "retirado_por",
                schema: "activos_fijos",
                table: "operaciones_libro");
        }
    }
}
