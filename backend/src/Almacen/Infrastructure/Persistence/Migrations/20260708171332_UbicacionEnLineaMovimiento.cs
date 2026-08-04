using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Almacen.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// ADR-0047 C7.2a — bin explícito en la línea de movimiento. Columna
    /// <c>ubicacion_id UUID NULL</c> + FK a <c>almacen.ubicaciones</c>
    /// (Restrict) + índice. <b>Sin backfill</b>: el histórico queda NULL a
    /// propósito ("vino por la ÚNICA" — todo el saldo previo vive en las
    /// ubicaciones <c>es_default</c>). El trigger consume la columna en la
    /// migración siguiente (TriggerBinExplicito); los handlers empiezan a
    /// poblarla en C7.2b/C7.2c.
    /// </summary>
    public partial class UbicacionEnLineaMovimiento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ubicacion_id",
                schema: "almacen",
                table: "lineas_movimiento",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_lineas_movimiento_ubicacion_id",
                schema: "almacen",
                table: "lineas_movimiento",
                column: "ubicacion_id");

            migrationBuilder.AddForeignKey(
                name: "fk_lineas_movimiento_ubicaciones_ubicacion_id",
                schema: "almacen",
                table: "lineas_movimiento",
                column: "ubicacion_id",
                principalSchema: "almacen",
                principalTable: "ubicaciones",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_lineas_movimiento_ubicaciones_ubicacion_id",
                schema: "almacen",
                table: "lineas_movimiento");

            migrationBuilder.DropIndex(
                name: "ix_lineas_movimiento_ubicacion_id",
                schema: "almacen",
                table: "lineas_movimiento");

            migrationBuilder.DropColumn(
                name: "ubicacion_id",
                schema: "almacen",
                table: "lineas_movimiento");
        }
    }
}
