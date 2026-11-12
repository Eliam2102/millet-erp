using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ServiciosComercioExterior : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "es_servicio_comercio_exterior",
                schema: "cuentas_por_pagar",
                table: "facturas_proveedor",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "es_servicio_comercio_exterior",
                schema: "cuentas_por_pagar",
                table: "facturas_proveedor");
        }
    }
}
