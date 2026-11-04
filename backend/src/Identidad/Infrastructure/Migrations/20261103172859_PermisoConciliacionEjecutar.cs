using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Identidad.Infrastructure.Migrations
{
    /// <summary>
    /// C7.7 (86e3jvv4q): permiso para conciliar la cartera a demanda. Supuesto a
    /// validar con el área de Crédito: lo reciben los roles que registran la
    /// cobranza (cuentas_por_cobrar.cobranza.registrar), que son los que aclaran las
    /// diferencias; SuperAdmin lo recibe al arrancar con el resto del catálogo.
    /// </summary>
    public partial class PermisoConciliacionEjecutar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "identidad",
                table: "permisos",
                columns: new[] { "id", "accion", "codigo", "created_at", "created_by", "deleted_at", "descripcion", "modulo", "recurso", "updated_at", "updated_by", "version" },
                values: new object[] { new Guid("0000000a-0007-0000-0000-000000000001"), "ejecutar", "cuentas_por_cobrar.conciliacion.ejecutar", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Ejecutar a demanda la conciliación diaria de cartera contra facturación", "cuentas_por_cobrar", "conciliacion", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 });

            migrationBuilder.Sql("""
                INSERT INTO identidad.rol_permisos (id, rol_id, permiso_id, version, created_at, updated_at, created_by, updated_by)
                SELECT gen_random_uuid(), rp.rol_id, nuevo.id, 1, now(), now(), 'seed', 'seed'
                FROM identidad.permisos anterior
                JOIN identidad.rol_permisos rp ON rp.permiso_id = anterior.id AND rp.deleted_at IS NULL
                JOIN identidad.permisos nuevo ON nuevo.codigo = 'cuentas_por_cobrar.conciliacion.ejecutar'
                WHERE anterior.codigo = 'cuentas_por_cobrar.cobranza.registrar'
                ON CONFLICT (rol_id, permiso_id) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM identidad.rol_permisos
                WHERE permiso_id = '0000000a-0007-0000-0000-000000000001';
                """);

            migrationBuilder.DeleteData(
                schema: "identidad",
                table: "permisos",
                keyColumn: "id",
                keyValue: new Guid("0000000a-0007-0000-0000-000000000001"));
        }
    }
}
