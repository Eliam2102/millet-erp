using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Almacen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgregarUbicacionHelperEnMovimientoInventario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ubicacion_helper_id",
                schema: "almacen",
                table: "movimientos_inventario",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_movimientos_inventario_ubicacion_helper_id",
                schema: "almacen",
                table: "movimientos_inventario",
                column: "ubicacion_helper_id");

            migrationBuilder.AddForeignKey(
                name: "fk_movimientos_inventario_ubicaciones_ubicacion_helper_id",
                schema: "almacen",
                table: "movimientos_inventario",
                column: "ubicacion_helper_id",
                principalSchema: "almacen",
                principalTable: "ubicaciones",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_movimientos_inventario_ubicaciones_ubicacion_helper_id",
                schema: "almacen",
                table: "movimientos_inventario");

            migrationBuilder.DropIndex(
                name: "ix_movimientos_inventario_ubicacion_helper_id",
                schema: "almacen",
                table: "movimientos_inventario");

            migrationBuilder.DropColumn(
                name: "ubicacion_helper_id",
                schema: "almacen",
                table: "movimientos_inventario");
        }
    }
}
