using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AF01LineasActivos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "clase_activo_id",
                schema: "compras",
                table: "requisicion_lineas",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "es_activo_fijo",
                schema: "compras",
                table: "requisicion_lineas",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "clase_activo_id",
                schema: "compras",
                table: "orden_compra_lineas",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "es_activo_fijo",
                schema: "compras",
                table: "orden_compra_lineas",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "clase_activo_id",
                schema: "compras",
                table: "requisicion_lineas");

            migrationBuilder.DropColumn(
                name: "es_activo_fijo",
                schema: "compras",
                table: "requisicion_lineas");

            migrationBuilder.DropColumn(
                name: "clase_activo_id",
                schema: "compras",
                table: "orden_compra_lineas");

            migrationBuilder.DropColumn(
                name: "es_activo_fijo",
                schema: "compras",
                table: "orden_compra_lineas");
        }
    }
}
