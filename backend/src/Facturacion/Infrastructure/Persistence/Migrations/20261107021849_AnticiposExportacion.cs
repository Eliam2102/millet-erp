using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AnticiposExportacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "tasa_iva_traslado",
                schema: "facturacion",
                table: "nota_credito",
                type: "numeric(7,6)",
                precision: 7,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "tasa_iva_traslado",
                schema: "facturacion",
                table: "factura_anticipo",
                type: "numeric(7,6)",
                precision: 7,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "receptor_num_reg_id_trib",
                schema: "facturacion",
                table: "comprobante",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "tasa_iva_traslado",
                schema: "facturacion",
                table: "nota_credito");

            migrationBuilder.DropColumn(
                name: "tasa_iva_traslado",
                schema: "facturacion",
                table: "factura_anticipo");

            migrationBuilder.DropColumn(
                name: "receptor_num_reg_id_trib",
                schema: "facturacion",
                table: "comprobante");
        }
    }
}
