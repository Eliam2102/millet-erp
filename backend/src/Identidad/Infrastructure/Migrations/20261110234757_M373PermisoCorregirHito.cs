using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Identidad.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class M373PermisoCorregirHito : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "identidad",
                table: "permisos",
                columns: new[] { "id", "accion", "codigo", "created_at", "created_by", "deleted_at", "descripcion", "modulo", "recurso", "updated_at", "updated_by", "version" },
                values: new object[] { new Guid("0000000e-0001-0000-0000-00000000000c"), "hitos-corregir", "comercio_exterior.expedientes.hitos-corregir", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Corregir la fecha real o la omisión de un hito con motivo", "comercio_exterior", "expedientes", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 });

            // M-373: quién autoriza la corrección sigue «Pendiente» con Millet (M-151, 10-nov). Por omisión la reciben
            // los roles de Gerencia de Comercio exterior (quienes aprueban el cierre del expediente); si ninguno la
            // tiene, los de Dirección de Administración y Finanzas (autorizan reversas de activos fijos). Cuando
            // Millet conteste, basta reasignar el permiso desde Administración › Roles, sin código.
            migrationBuilder.Sql("""
                INSERT INTO identidad.rol_permisos (id, rol_id, permiso_id, created_at, updated_at, created_by, version)
                SELECT DISTINCT ON (rp.rol_id) gen_random_uuid(), rp.rol_id, '0000000e-0001-0000-0000-00000000000c', now(), now(), 'seed-m373', 1
                FROM identidad.rol_permisos rp
                WHERE rp.permiso_id = '0000000e-0007-0000-0000-000000000002' AND rp.deleted_at IS NULL
                ON CONFLICT (rol_id, permiso_id) DO NOTHING;
                INSERT INTO identidad.rol_permisos (id, rol_id, permiso_id, created_at, updated_at, created_by, version)
                SELECT DISTINCT ON (rp.rol_id) gen_random_uuid(), rp.rol_id, '0000000e-0001-0000-0000-00000000000c', now(), now(), 'seed-m373', 1
                FROM identidad.rol_permisos rp
                WHERE rp.permiso_id = '0000000f-0001-0000-0000-000000000032' AND rp.deleted_at IS NULL
                  AND NOT EXISTS (SELECT 1 FROM identidad.rol_permisos g WHERE g.permiso_id = '0000000e-0007-0000-0000-000000000002' AND g.deleted_at IS NULL)
                ON CONFLICT (rol_id, permiso_id) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM identidad.rol_permisos WHERE permiso_id = '0000000e-0001-0000-0000-00000000000c' AND created_by = 'seed-m373';");
            migrationBuilder.DeleteData(
                schema: "identidad",
                table: "permisos",
                keyColumn: "id",
                keyValue: new Guid("0000000e-0001-0000-0000-00000000000c"));
        }
    }
}
