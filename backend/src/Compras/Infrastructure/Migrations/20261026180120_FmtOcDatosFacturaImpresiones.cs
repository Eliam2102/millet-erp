using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FmtOcDatosFacturaImpresiones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "forma_pago_clave",
                schema: "compras",
                table: "ordenes_compra",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "metodo_pago",
                schema: "compras",
                table: "ordenes_compra",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "uso_cfdi_clave",
                schema: "compras",
                table: "ordenes_compra",
                type: "character varying(4)",
                maxLength: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "impresiones",
                schema: "compras",
                table: "orden_compra_pdf",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ultima_impresion_en",
                schema: "compras",
                table: "orden_compra_pdf",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ultima_impresion_por",
                schema: "compras",
                table: "orden_compra_pdf",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "forma_pago_clave",
                schema: "compras",
                table: "ordenes_compra");

            migrationBuilder.DropColumn(
                name: "metodo_pago",
                schema: "compras",
                table: "ordenes_compra");

            migrationBuilder.DropColumn(
                name: "uso_cfdi_clave",
                schema: "compras",
                table: "ordenes_compra");

            migrationBuilder.DropColumn(
                name: "impresiones",
                schema: "compras",
                table: "orden_compra_pdf");

            migrationBuilder.DropColumn(
                name: "ultima_impresion_en",
                schema: "compras",
                table: "orden_compra_pdf");

            migrationBuilder.DropColumn(
                name: "ultima_impresion_por",
                schema: "compras",
                table: "orden_compra_pdf");
        }
    }
}
