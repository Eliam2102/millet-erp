using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Identidad.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PermisoReporteAnalisisProveedores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "identidad",
                table: "permisos",
                columns: new[] { "id", "accion", "codigo", "created_at", "created_by", "deleted_at", "descripcion", "modulo", "recurso", "updated_at", "updated_by", "version" },
                values: new object[] { new Guid("00000003-0003-0000-0000-00000000000f"), "reportes-analisis-proveedores", "compras.ordenes.reportes-analisis-proveedores", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Consultar el análisis de compras, saldos pendientes y cumplimiento de proveedores", "compras", "ordenes", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 });

            // Los roles son datos de runtime: el reporte se concede a quien ya
            // consulta partidas abiertas de OC (Compras) o la bitácora de
            // auditoría (plano 1.7.1: "Compras o auditor").
            migrationBuilder.Sql("""
                INSERT INTO identidad.rol_permisos
                    (id, rol_id, permiso_id, version, created_at, updated_at, created_by, updated_by)
                SELECT gen_random_uuid(), rp.rol_id, '00000003-0003-0000-0000-00000000000f', 1, now(), now(),
                    'MigracionReporteAnalisisProveedores', 'MigracionReporteAnalisisProveedores'
                FROM identidad.rol_permisos rp
                WHERE rp.deleted_at IS NULL
                  AND rp.permiso_id IN ('00000003-0003-0000-0000-00000000000a', '00000005-0002-0000-0000-000000000001')
                  AND NOT EXISTS (
                      SELECT 1 FROM identidad.rol_permisos x
                      WHERE x.rol_id = rp.rol_id AND x.permiso_id = '00000003-0003-0000-0000-00000000000f')
                GROUP BY rp.rol_id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM identidad.rol_permisos WHERE permiso_id = '00000003-0003-0000-0000-00000000000f';");

            migrationBuilder.DeleteData(
                schema: "identidad",
                table: "permisos",
                keyColumn: "id",
                keyValue: new Guid("00000003-0003-0000-0000-00000000000f"));
        }
    }
}
