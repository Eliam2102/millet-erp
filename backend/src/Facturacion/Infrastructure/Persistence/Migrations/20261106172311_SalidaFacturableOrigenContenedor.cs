using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SalidaFacturableOrigenContenedor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_salida_facturable_referencia",
                schema: "facturacion",
                table: "salida_facturable");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "descartada_en",
                schema: "facturacion",
                table: "salida_facturable",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "motivo_descarte",
                schema: "facturacion",
                table: "salida_facturable",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "origen_id",
                schema: "facturacion",
                table: "salida_facturable",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_salida_facturable_origen_pedido",
                schema: "facturacion",
                table: "salida_facturable",
                columns: new[] { "empresa_id", "origen", "origen_id", "pedido_facturable_id" },
                unique: true,
                filter: "origen_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_salida_facturable_referencia",
                schema: "facturacion",
                table: "salida_facturable",
                columns: new[] { "empresa_id", "origen", "referencia" },
                unique: true,
                filter: "origen_id IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_salida_facturable_origen_pedido",
                schema: "facturacion",
                table: "salida_facturable");

            migrationBuilder.DropIndex(
                name: "ux_salida_facturable_referencia",
                schema: "facturacion",
                table: "salida_facturable");

            migrationBuilder.DropColumn(
                name: "descartada_en",
                schema: "facturacion",
                table: "salida_facturable");

            migrationBuilder.DropColumn(
                name: "motivo_descarte",
                schema: "facturacion",
                table: "salida_facturable");

            migrationBuilder.DropColumn(
                name: "origen_id",
                schema: "facturacion",
                table: "salida_facturable");

            migrationBuilder.CreateIndex(
                name: "ux_salida_facturable_referencia",
                schema: "facturacion",
                table: "salida_facturable",
                columns: new[] { "empresa_id", "origen", "referencia" },
                unique: true);
        }
    }
}
