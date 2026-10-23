using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UmbralModoNivel2PorDepartamento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "modo_nivel2",
                schema: "compras",
                table: "settings");

            migrationBuilder.AddColumn<int>(
                name: "modo_nivel2",
                schema: "compras",
                table: "umbrales_aprobacion_departamento",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<decimal>(
                name: "monto_minimo_disponibilidad",
                schema: "compras",
                table: "umbrales_aprobacion_departamento",
                type: "numeric(15,2)",
                precision: 15,
                scale: 2,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_umbrales_monto_minimo_no_negativo",
                schema: "compras",
                table: "umbrales_aprobacion_departamento",
                sql: "monto_minimo_disponibilidad IS NULL OR monto_minimo_disponibilidad >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_umbrales_monto_minimo_no_negativo",
                schema: "compras",
                table: "umbrales_aprobacion_departamento");

            migrationBuilder.DropColumn(
                name: "modo_nivel2",
                schema: "compras",
                table: "umbrales_aprobacion_departamento");

            migrationBuilder.DropColumn(
                name: "monto_minimo_disponibilidad",
                schema: "compras",
                table: "umbrales_aprobacion_departamento");

            migrationBuilder.AddColumn<int>(
                name: "modo_nivel2",
                schema: "compras",
                table: "settings",
                type: "integer",
                nullable: false,
                defaultValue: 1);
        }
    }
}
