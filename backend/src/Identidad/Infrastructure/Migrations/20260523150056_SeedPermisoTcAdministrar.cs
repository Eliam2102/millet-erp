using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Identidad.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SeedPermisoTcAdministrar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "identidad",
                table: "permisos",
                columns: new[] { "id", "accion", "codigo", "created_at", "created_by", "deleted_at", "descripcion", "modulo", "recurso", "updated_at", "updated_by", "version" },
                values: new object[] { new Guid("00000007-0006-0000-0000-000000000004"), "administrar", "cuentas_por_pagar.tc.administrar", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Administrar tarjetas corporativas y usuarios autorizados (F7-PR4)", "cuentas_por_pagar", "tc", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "identidad",
                table: "permisos",
                keyColumn: "id",
                keyValue: new Guid("00000007-0006-0000-0000-000000000004"));
        }
    }
}
