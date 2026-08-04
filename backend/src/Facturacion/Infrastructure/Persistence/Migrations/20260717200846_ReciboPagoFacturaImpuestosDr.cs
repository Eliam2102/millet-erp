using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReciboPagoFacturaImpuestosDr : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "base_gravable_pagada",
                schema: "facturacion",
                table: "recibo_pago_factura",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "equivalencia",
                schema: "facturacion",
                table: "recibo_pago_factura",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "objeto_imp_dr",
                schema: "facturacion",
                table: "recibo_pago_factura",
                type: "character varying(2)",
                maxLength: 2,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "recibo_pago_factura_impuesto",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    recibo_pago_factura_id = table.Column<Guid>(type: "uuid", nullable: false),
                    impuesto = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    tipo_factor = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    tasa_o_cuota = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    es_retencion = table.Column<bool>(type: "boolean", nullable: false),
                    base_dr = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    importe_dr = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recibo_pago_factura_impuesto", x => x.id);
                    table.ForeignKey(
                        name: "fk_recibo_pago_factura_impuesto_recibo_pago_factura_recibo_pag",
                        column: x => x.recibo_pago_factura_id,
                        principalSchema: "facturacion",
                        principalTable: "recibo_pago_factura",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_recibo_pago_factura_impuesto_doc",
                schema: "facturacion",
                table: "recibo_pago_factura_impuesto",
                column: "recibo_pago_factura_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recibo_pago_factura_impuesto",
                schema: "facturacion");

            migrationBuilder.DropColumn(
                name: "base_gravable_pagada",
                schema: "facturacion",
                table: "recibo_pago_factura");

            migrationBuilder.DropColumn(
                name: "equivalencia",
                schema: "facturacion",
                table: "recibo_pago_factura");

            migrationBuilder.DropColumn(
                name: "objeto_imp_dr",
                schema: "facturacion",
                table: "recibo_pago_factura");
        }
    }
}
