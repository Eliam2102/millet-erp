using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Identidad.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PermisoReportesLeerTodasSucursales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "identidad",
                table: "permisos",
                columns: new[] { "id", "accion", "codigo", "created_at", "created_by", "deleted_at", "descripcion", "modulo", "recurso", "updated_at", "updated_by", "version" },
                values: new object[] { new Guid("00000009-000a-0000-0000-000000000002"), "leer-todas-sucursales", "facturacion.reportes.leer-todas-sucursales", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Consultar el cierre del día (monitor de CFDI y facturado contra ingresado) de todas las sucursales, sin estar asociado a ellas", "facturacion", "reportes", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 });

            // F6.7 (ADR-0051): los roles corporativos que ya ven todas las cajas
            // (facturacion.caja.leer-todas) ven también el cierre del día de todas las sucursales.
            migrationBuilder.Sql("""
                INSERT INTO identidad.rol_permisos (id, rol_id, permiso_id, created_at, updated_at, created_by, version)
                SELECT gen_random_uuid(), rp.rol_id, '00000009-000a-0000-0000-000000000002', now(), now(), 'seed-f6.7', 1
                FROM identidad.rol_permisos rp
                WHERE rp.permiso_id = '00000009-0009-0000-0000-000000000005' AND rp.deleted_at IS NULL
                ON CONFLICT (rol_id, permiso_id) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // rol_permisos y usuario_permiso_overrides.permiso_id son FK Restrict.
            migrationBuilder.Sql("DELETE FROM identidad.rol_permisos WHERE permiso_id = '00000009-000a-0000-0000-000000000002';");
            migrationBuilder.Sql("DELETE FROM identidad.usuario_permiso_overrides WHERE permiso_id = '00000009-000a-0000-0000-000000000002';");
            migrationBuilder.DeleteData(
                schema: "identidad",
                table: "permisos",
                keyColumn: "id",
                keyValue: new Guid("00000009-000a-0000-0000-000000000002"));
        }
    }
}
