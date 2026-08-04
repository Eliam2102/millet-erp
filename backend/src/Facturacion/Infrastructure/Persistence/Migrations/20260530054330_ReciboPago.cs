using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReciboPago : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "recibo_pago",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha_pago = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    importe_total_pago = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    moneda_pago = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recibo_pago", x => x.id);
                    table.ForeignKey(
                        name: "fk_recibo_pago_comprobante_id",
                        column: x => x.id,
                        principalSchema: "facturacion",
                        principalTable: "comprobante",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recibo_pago_factura",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    recibo_pago_id = table.Column<Guid>(type: "uuid", nullable: false),
                    factura_venta_id = table.Column<Guid>(type: "uuid", nullable: false),
                    factura_uuid = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    num_parcialidad = table.Column<int>(type: "integer", nullable: false),
                    moneda_factura = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    importe_pagado = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    saldo_anterior = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    saldo_insoluto = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    forma_pago_real = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    tc_pago = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    ganancia_perdida_cambiaria = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    cuenta_ordenante = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    cuenta_beneficiaria = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    referencia_pago = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recibo_pago_factura", x => x.id);
                    table.ForeignKey(
                        name: "fk_recibo_pago_factura_recibos_pago_recibo_pago_id",
                        column: x => x.recibo_pago_id,
                        principalSchema: "facturacion",
                        principalTable: "recibo_pago",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_recibo_pago_factura_factura",
                schema: "facturacion",
                table: "recibo_pago_factura",
                column: "factura_venta_id");

            migrationBuilder.CreateIndex(
                name: "ix_recibo_pago_factura_recibo_pago_id",
                schema: "facturacion",
                table: "recibo_pago_factura",
                column: "recibo_pago_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recibo_pago_factura",
                schema: "facturacion");

            migrationBuilder.DropTable(
                name: "recibo_pago",
                schema: "facturacion");
        }
    }
}
