using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class AgregarSugerenciasRolesPuestos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "puestos",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "rol_sugerido_id",
                value: new Guid("00000002-0003-0000-0000-000000000001"));

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "puestos",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000002"),
                column: "rol_sugerido_id",
                value: new Guid("00000002-0003-0000-0000-000000000003"));

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "puestos",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000003"),
                column: "rol_sugerido_id",
                value: new Guid("00000002-0003-0000-0000-000000000004"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "puestos",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "rol_sugerido_id",
                value: null);

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "puestos",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000002"),
                column: "rol_sugerido_id",
                value: null);

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "puestos",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000003"),
                column: "rol_sugerido_id",
                value: null);
        }
    }
}
