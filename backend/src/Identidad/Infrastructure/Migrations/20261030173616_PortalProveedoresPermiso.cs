using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Identidad.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PortalProveedoresPermiso : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "identidad",
                table: "permisos",
                columns: new[] { "id", "accion", "codigo", "created_at", "created_by", "deleted_at", "descripcion", "modulo", "recurso", "updated_at", "updated_by", "version" },
                values: new object[] { new Guid("00000007-0011-0000-0000-000000000001"), "administrar", "cuentas_por_pagar.portal.administrar", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Administrar el acceso de un proveedor al portal de proveedores (correos autorizados)", "cuentas_por_pagar", "portal", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 });
            // Los roles que ponen proveedores en revisión (responsable de CxP) reciben el permiso del portal.
            migrationBuilder.Sql("""
                INSERT INTO identidad.rol_permisos (id, rol_id, permiso_id, version, created_at, updated_at, created_by, updated_by)
                SELECT gen_random_uuid(), rp.rol_id, nuevo.id, 1, now(), now(), 'seed', 'seed'
                FROM identidad.permisos anterior
                JOIN identidad.rol_permisos rp ON rp.permiso_id = anterior.id AND rp.deleted_at IS NULL
                JOIN identidad.permisos nuevo ON nuevo.codigo = 'cuentas_por_pagar.portal.administrar'
                WHERE anterior.codigo = 'cuentas_por_pagar.proveedores.poner-revision'
                ON CONFLICT (rol_id, permiso_id) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM identidad.rol_permisos
                WHERE permiso_id IN (SELECT id FROM identidad.permisos WHERE codigo = 'cuentas_por_pagar.portal.administrar');
                """);
            migrationBuilder.DeleteData(
                schema: "identidad",
                table: "permisos",
                keyColumn: "id",
                keyValue: new Guid("00000007-0011-0000-0000-000000000001"));
        }
    }
}
