using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorCobrar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClasificacionLineaCredito : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "clasificacion",
                schema: "cuentas_por_cobrar",
                table: "linea_credito",
                type: "character varying(1)",
                maxLength: 1,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_linea_credito_clasificacion",
                schema: "cuentas_por_cobrar",
                table: "linea_credito",
                sql: "clasificacion IS NULL OR clasificacion IN ('A', 'B', 'C', 'E')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_linea_credito_clasificacion",
                schema: "cuentas_por_cobrar",
                table: "linea_credito");

            migrationBuilder.DropColumn(
                name: "clasificacion",
                schema: "cuentas_por_cobrar",
                table: "linea_credito");
        }
    }
}
