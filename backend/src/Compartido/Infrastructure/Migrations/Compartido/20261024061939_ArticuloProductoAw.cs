using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class ArticuloProductoAw : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "producto_aw_id",
                schema: "compartido",
                table: "articulos",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_articulos_producto_aw_id",
                schema: "compartido",
                table: "articulos",
                column: "producto_aw_id",
                unique: true,
                filter: "producto_aw_id IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_articulos_productos_aw_producto_aw_id",
                schema: "compartido",
                table: "articulos",
                column: "producto_aw_id",
                principalSchema: "compartido",
                principalTable: "producto_aw",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_articulos_productos_aw_producto_aw_id",
                schema: "compartido",
                table: "articulos");

            migrationBuilder.DropIndex(
                name: "ux_articulos_producto_aw_id",
                schema: "compartido",
                table: "articulos");

            migrationBuilder.DropColumn(
                name: "producto_aw_id",
                schema: "compartido",
                table: "articulos");
        }
    }
}
