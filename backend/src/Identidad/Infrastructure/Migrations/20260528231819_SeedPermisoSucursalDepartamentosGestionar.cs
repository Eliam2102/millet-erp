using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Identidad.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SeedPermisoSucursalDepartamentosGestionar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "identidad",
                table: "permisos",
                columns: new[] { "id", "accion", "codigo", "created_at", "created_by", "deleted_at", "descripcion", "modulo", "recurso", "updated_at", "updated_by", "version" },
                values: new object[] { new Guid("00000005-0006-0000-0000-000000000001"), "departamentos-gestionar", "admin.sucursales.departamentos-gestionar", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Asignar, desactivar y reactivar departamentos por sucursal (N:M)", "admin", "sucursales", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "identidad",
                table: "permisos",
                keyColumn: "id",
                keyValue: new Guid("00000005-0006-0000-0000-000000000001"));
        }
    }
}
