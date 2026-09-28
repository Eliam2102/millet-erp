using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class PuestoEnVariosDepartamentosPorSucursal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_sucursal_puestos_sucursal_id_puesto_id",
                schema: "compartido",
                table: "sucursal_puestos");

            migrationBuilder.AddColumn<Guid>(
                name: "rol_sugerido_id",
                schema: "compartido",
                table: "sucursal_puestos",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_sucursal_puestos_rol_sugerido_id",
                schema: "compartido",
                table: "sucursal_puestos",
                column: "rol_sugerido_id");

            migrationBuilder.CreateIndex(
                name: "ix_sucursal_puestos_sucursal_id_puesto_id_departamento_id",
                schema: "compartido",
                table: "sucursal_puestos",
                columns: new[] { "sucursal_id", "puesto_id", "departamento_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_sucursal_puestos_rol_sugerido_id",
                schema: "compartido",
                table: "sucursal_puestos");

            migrationBuilder.DropIndex(
                name: "ix_sucursal_puestos_sucursal_id_puesto_id_departamento_id",
                schema: "compartido",
                table: "sucursal_puestos");

            migrationBuilder.DropColumn(
                name: "rol_sugerido_id",
                schema: "compartido",
                table: "sucursal_puestos");

            migrationBuilder.CreateIndex(
                name: "ix_sucursal_puestos_sucursal_id_puesto_id",
                schema: "compartido",
                table: "sucursal_puestos",
                columns: new[] { "sucursal_id", "puesto_id" },
                unique: true);
        }
    }
}
