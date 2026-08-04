using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MetodoPagoCfdiFactura : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "metodo_pago",
                schema: "cuentas_por_pagar",
                table: "facturas_proveedor",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "metodo_pago",
                schema: "cuentas_por_pagar",
                table: "cfdis_recibidos",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "metodo_pago",
                schema: "cuentas_por_pagar",
                table: "facturas_proveedor");

            migrationBuilder.DropColumn(
                name: "metodo_pago",
                schema: "cuentas_por_pagar",
                table: "cfdis_recibidos");
        }
    }
}
