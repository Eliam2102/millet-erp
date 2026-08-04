using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropAlmacenDestinoDeOc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_oc_lineas_almacen",
                schema: "compras",
                table: "orden_compra_lineas");

            migrationBuilder.DropColumn(
                name: "almacen_destino_default_id",
                schema: "compras",
                table: "ordenes_compra");

            migrationBuilder.DropColumn(
                name: "almacen_destino_id",
                schema: "compras",
                table: "orden_compra_lineas");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "almacen_destino_default_id",
                schema: "compras",
                table: "ordenes_compra",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "almacen_destino_id",
                schema: "compras",
                table: "orden_compra_lineas",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "ix_oc_lineas_almacen",
                schema: "compras",
                table: "orden_compra_lineas",
                column: "almacen_destino_id");
        }
    }
}
