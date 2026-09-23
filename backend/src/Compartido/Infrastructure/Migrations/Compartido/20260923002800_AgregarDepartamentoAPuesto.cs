using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class AgregarDepartamentoAPuesto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "departamento_id",
                schema: "compartido",
                table: "puestos",
                type: "uuid",
                nullable: true);

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "puestos",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "departamento_id",
                value: null);

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "puestos",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000002"),
                column: "departamento_id",
                value: null);

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "puestos",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000003"),
                column: "departamento_id",
                value: null);

            migrationBuilder.CreateIndex(
                name: "ix_puestos_departamento_id",
                schema: "compartido",
                table: "puestos",
                column: "departamento_id");

            migrationBuilder.AddForeignKey(
                name: "fk_puestos_departamentos_departamento_id",
                schema: "compartido",
                table: "puestos",
                column: "departamento_id",
                principalSchema: "compartido",
                principalTable: "departamentos",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_puestos_departamentos_departamento_id",
                schema: "compartido",
                table: "puestos");

            migrationBuilder.DropIndex(
                name: "ix_puestos_departamento_id",
                schema: "compartido",
                table: "puestos");

            migrationBuilder.DropColumn(
                name: "departamento_id",
                schema: "compartido",
                table: "puestos");
        }
    }
}
