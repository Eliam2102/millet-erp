using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Identidad.Infrastructure.Migrations
{
    /// <summary>
    /// C7.2 (86e3kdxy6): permiso para administrar las reglas de liberación por
    /// serie. Supuesto a validar con el área de Crédito: lo reciben los roles
    /// que ya autorizan liberaciones (cuentas_por_cobrar.liberacion.override);
    /// SuperAdmin lo recibe al arrancar con el resto del catálogo.
    /// </summary>
    public partial class PermisoReglasSerieGestionar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "identidad",
                table: "permisos",
                columns: new[] { "id", "accion", "codigo", "created_at", "created_by", "deleted_at", "descripcion", "modulo", "recurso", "updated_at", "updated_by", "version" },
                values: new object[] { new Guid("0000000a-0006-0000-0000-000000000001"), "gestionar", "cuentas_por_cobrar.reglas-serie.gestionar", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Dar de alta, cambiar y activar o desactivar las reglas de liberación por serie", "cuentas_por_cobrar", "reglas-serie", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 });

            migrationBuilder.Sql("""
                INSERT INTO identidad.rol_permisos (id, rol_id, permiso_id, version, created_at, updated_at, created_by, updated_by)
                SELECT gen_random_uuid(), rp.rol_id, nuevo.id, 1, now(), now(), 'seed', 'seed'
                FROM identidad.permisos anterior
                JOIN identidad.rol_permisos rp ON rp.permiso_id = anterior.id AND rp.deleted_at IS NULL
                JOIN identidad.permisos nuevo ON nuevo.codigo = 'cuentas_por_cobrar.reglas-serie.gestionar'
                WHERE anterior.codigo = 'cuentas_por_cobrar.liberacion.override'
                ON CONFLICT (rol_id, permiso_id) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM identidad.rol_permisos
                WHERE permiso_id = '0000000a-0006-0000-0000-000000000001';
                """);

            migrationBuilder.DeleteData(
                schema: "identidad",
                table: "permisos",
                keyColumn: "id",
                keyValue: new Guid("0000000a-0006-0000-0000-000000000001"));
        }
    }
}
