using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CajaDevolucionFaltante : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "corte_origen_sesion_id",
                schema: "facturacion",
                table: "caja_movimiento",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_caja_movimiento_corte_origen",
                schema: "facturacion",
                table: "caja_movimiento",
                column: "corte_origen_sesion_id",
                filter: "corte_origen_sesion_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_caja_movimiento_corte_origen",
                schema: "facturacion",
                table: "caja_movimiento");

            migrationBuilder.DropColumn(
                name: "corte_origen_sesion_id",
                schema: "facturacion",
                table: "caja_movimiento");
        }
    }
}
