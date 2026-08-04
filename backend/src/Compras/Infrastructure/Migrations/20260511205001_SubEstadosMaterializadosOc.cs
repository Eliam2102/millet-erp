using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SubEstadosMaterializadosOc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "fecha_cierre",
                schema: "compras",
                table: "ordenes_compra",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "monto_pagado",
                schema: "compras",
                table: "ordenes_compra",
                type: "numeric(15,2)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "fecha_cierre",
                schema: "compras",
                table: "ordenes_compra");

            migrationBuilder.DropColumn(
                name: "monto_pagado",
                schema: "compras",
                table: "ordenes_compra");
        }
    }
}
