using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CantidadEntregadaEnLineaRq : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "cant_entregada",
                schema: "compras",
                table: "requisicion_lineas",
                type: "numeric(18,5)",
                precision: 18,
                scale: 5,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddCheckConstraint(
                name: "ck_requisicion_lineas_entregada_no_negativa",
                schema: "compras",
                table: "requisicion_lineas",
                sql: "cant_entregada >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_requisicion_lineas_entregada_no_negativa",
                schema: "compras",
                table: "requisicion_lineas");

            migrationBuilder.DropColumn(
                name: "cant_entregada",
                schema: "compras",
                table: "requisicion_lineas");
        }
    }
}
