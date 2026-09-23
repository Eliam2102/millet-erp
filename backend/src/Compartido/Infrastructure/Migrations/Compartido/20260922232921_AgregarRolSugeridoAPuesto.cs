using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class AgregarRolSugeridoAPuesto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "rol_sugerido_id",
                schema: "compartido",
                table: "puestos",
                type: "uuid",
                nullable: true);

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

            migrationBuilder.CreateIndex(
                name: "ix_puestos_rol_sugerido_id",
                schema: "compartido",
                table: "puestos",
                column: "rol_sugerido_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_puestos_rol_sugerido_id",
                schema: "compartido",
                table: "puestos");

            migrationBuilder.DropColumn(
                name: "rol_sugerido_id",
                schema: "compartido",
                table: "puestos");
        }
    }
}
