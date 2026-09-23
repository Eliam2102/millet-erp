using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class AgregarDepartamentoASucursalPuesto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "departamento_id",
                schema: "compartido",
                table: "sucursal_puestos",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(@"
                UPDATE compartido.sucursal_puestos sp
                SET departamento_id = p.departamento_id
                FROM compartido.puestos p
                WHERE sp.puesto_id = p.id AND p.departamento_id IS NOT NULL;

                UPDATE compartido.sucursal_puestos sp
                SET departamento_id = (
                    SELECT d.id FROM compartido.departamentos d
                    WHERE d.empresa_id = sp.empresa_id AND d.estatus = 0
                    ORDER BY d.created_at ASC
                    LIMIT 1
                )
                WHERE sp.departamento_id IS NULL;
            ");

            migrationBuilder.AlterColumn<Guid>(
                name: "departamento_id",
                schema: "compartido",
                table: "sucursal_puestos",
                type: "uuid",
                nullable: false);

            migrationBuilder.CreateIndex(
                name: "ix_sucursal_puestos_departamento_id",
                schema: "compartido",
                table: "sucursal_puestos",
                column: "departamento_id");

            migrationBuilder.AddForeignKey(
                name: "fk_sucursal_puestos_departamentos_departamento_id",
                schema: "compartido",
                table: "sucursal_puestos",
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
                name: "fk_sucursal_puestos_departamentos_departamento_id",
                schema: "compartido",
                table: "sucursal_puestos");

            migrationBuilder.DropIndex(
                name: "ix_sucursal_puestos_departamento_id",
                schema: "compartido",
                table: "sucursal_puestos");

            migrationBuilder.DropColumn(
                name: "departamento_id",
                schema: "compartido",
                table: "sucursal_puestos");
        }
    }
}
