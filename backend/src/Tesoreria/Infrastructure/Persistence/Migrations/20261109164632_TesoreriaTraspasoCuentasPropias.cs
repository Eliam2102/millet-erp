using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Tesoreria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TesoreriaTraspasoCuentasPropias : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_concepto_movimiento_clasificacion",
                schema: "tesoreria",
                table: "concepto_movimiento");

            migrationBuilder.AddColumn<Guid>(
                name: "traspaso_id",
                schema: "tesoreria",
                table: "movimiento_bancario",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "traspaso_motivo",
                schema: "tesoreria",
                table: "movimiento_bancario",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.UpdateData(
                schema: "tesoreria",
                table: "concepto_movimiento",
                keyColumn: "id",
                keyValue: new Guid("0000000b-1001-0000-0000-000000000007"),
                column: "clasificacion_flujo",
                value: (short)4);

            migrationBuilder.CreateIndex(
                name: "ix_movimiento_bancario_traspaso",
                schema: "tesoreria",
                table: "movimiento_bancario",
                column: "traspaso_id",
                filter: "traspaso_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_concepto_movimiento_clasificacion",
                schema: "tesoreria",
                table: "concepto_movimiento",
                sql: "clasificacion_flujo IN (1, 2, 3, 4)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_movimiento_bancario_traspaso",
                schema: "tesoreria",
                table: "movimiento_bancario");

            migrationBuilder.DropCheckConstraint(
                name: "ck_concepto_movimiento_clasificacion",
                schema: "tesoreria",
                table: "concepto_movimiento");

            migrationBuilder.DropColumn(
                name: "traspaso_id",
                schema: "tesoreria",
                table: "movimiento_bancario");

            migrationBuilder.DropColumn(
                name: "traspaso_motivo",
                schema: "tesoreria",
                table: "movimiento_bancario");

            migrationBuilder.UpdateData(
                schema: "tesoreria",
                table: "concepto_movimiento",
                keyColumn: "id",
                keyValue: new Guid("0000000b-1001-0000-0000-000000000007"),
                column: "clasificacion_flujo",
                value: (short)1);

            migrationBuilder.AddCheckConstraint(
                name: "ck_concepto_movimiento_clasificacion",
                schema: "tesoreria",
                table: "concepto_movimiento",
                sql: "clasificacion_flujo IN (1, 2, 3)");
        }
    }
}
