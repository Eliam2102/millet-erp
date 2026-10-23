using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Identidad.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RetirarPermisoAjustarToleranciaCxp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // rol_permisos y usuario_permiso_overrides.permiso_id son FK Restrict: se borran primero las asignaciones.
            migrationBuilder.Sql(
                "DELETE FROM identidad.rol_permisos WHERE permiso_id = '00000007-0007-0000-0000-000000000003';");
            migrationBuilder.Sql(
                "DELETE FROM identidad.usuario_permiso_overrides WHERE permiso_id = '00000007-0007-0000-0000-000000000003';");

            migrationBuilder.DeleteData(
                schema: "identidad",
                table: "permisos",
                keyColumn: "id",
                keyValue: new Guid("00000007-0007-0000-0000-000000000003"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "identidad",
                table: "permisos",
                columns: new[] { "id", "accion", "codigo", "created_at", "created_by", "deleted_at", "descripcion", "modulo", "recurso", "updated_at", "updated_by", "version" },
                values: new object[] { new Guid("00000007-0007-0000-0000-000000000003"), "ajustar-tolerancia", "cuentas_por_pagar.proveedores.ajustar-tolerancia", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Ajustar tolerancia de conciliación por proveedor (restringido)", "cuentas_por_pagar", "proveedores", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 });
        }
    }
}
