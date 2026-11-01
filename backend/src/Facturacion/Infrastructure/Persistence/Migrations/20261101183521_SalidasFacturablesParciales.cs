using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SalidasFacturablesParciales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "salida_facturable_id",
                schema: "facturacion",
                table: "factura_venta",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "salida_facturable",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pedido_facturable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero_parcialidad = table.Column<int>(type: "integer", nullable: false),
                    origen = table.Column<short>(type: "smallint", nullable: false),
                    referencia = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    registrada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_salida_facturable", x => x.id);
                    table.ForeignKey(
                        name: "fk_salida_facturable_pedido_facturable_pedido_facturable_id",
                        column: x => x.pedido_facturable_id,
                        principalSchema: "facturacion",
                        principalTable: "pedido_facturable",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "salida_facturable_linea",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    salida_facturable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    posicion = table.Column<int>(type: "integer", nullable: false),
                    cantidad = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    importe = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_salida_facturable_linea", x => x.id);
                    table.ForeignKey(
                        name: "fk_salida_facturable_linea_salidas_facturables_salida_facturab",
                        column: x => x.salida_facturable_id,
                        principalSchema: "facturacion",
                        principalTable: "salida_facturable",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_factura_venta_salida",
                schema: "facturacion",
                table: "factura_venta",
                column: "salida_facturable_id",
                filter: "salida_facturable_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_salida_facturable_parcialidad",
                schema: "facturacion",
                table: "salida_facturable",
                columns: new[] { "pedido_facturable_id", "numero_parcialidad" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_salida_facturable_referencia",
                schema: "facturacion",
                table: "salida_facturable",
                columns: new[] { "empresa_id", "origen", "referencia" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_salida_facturable_linea_posicion",
                schema: "facturacion",
                table: "salida_facturable_linea",
                columns: new[] { "salida_facturable_id", "posicion" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "salida_facturable_linea",
                schema: "facturacion");

            migrationBuilder.DropTable(
                name: "salida_facturable",
                schema: "facturacion");

            migrationBuilder.DropIndex(
                name: "ix_factura_venta_salida",
                schema: "facturacion",
                table: "factura_venta");

            migrationBuilder.DropColumn(
                name: "salida_facturable_id",
                schema: "facturacion",
                table: "factura_venta");
        }
    }
}
