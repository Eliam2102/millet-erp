using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CotizacionesCompra : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cotizaciones",
                schema: "compras",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requisicion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    proveedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    folio_proveedor = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    fecha_cotizacion = table.Column<DateOnly>(type: "date", nullable: false),
                    vigente_hasta = table.Column<DateOnly>(type: "date", nullable: false),
                    condiciones_credito = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    registrada_por = table.Column<Guid>(type: "uuid", nullable: false),
                    registrada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    elegida_por = table.Column<Guid>(type: "uuid", nullable: true),
                    elegida_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    motivo_eleccion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    orden_compra_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cotizaciones", x => x.id);
                    table.CheckConstraint("ck_cotizaciones_eleccion", "(orden_compra_id IS NULL AND motivo_eleccion IS NULL) OR (orden_compra_id IS NOT NULL AND motivo_eleccion IS NOT NULL)");
                    table.CheckConstraint("ck_cotizaciones_moneda_iso", "moneda ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("ck_cotizaciones_vigencia", "vigente_hasta >= fecha_cotizacion");
                    table.ForeignKey(
                        name: "fk_cotizaciones_requisiciones_requisicion_id",
                        column: x => x.requisicion_id,
                        principalSchema: "compras",
                        principalTable: "requisiciones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cotizacion_lineas",
                schema: "compras",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cotizacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    linea_requisicion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unidad_cotizada = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    factor_a_unidad_rq = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    precio_unitario = table.Column<decimal>(type: "numeric(15,4)", nullable: false),
                    dias_entrega = table.Column<int>(type: "integer", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cotizacion_lineas", x => x.id);
                    table.CheckConstraint("ck_cotizacion_lineas_factor", "factor_a_unidad_rq > 0");
                    table.CheckConstraint("ck_cotizacion_lineas_precio", "precio_unitario > 0");
                    table.ForeignKey(
                        name: "fk_cotizacion_lineas_cotizaciones_cotizacion_id",
                        column: x => x.cotizacion_id,
                        principalSchema: "compras",
                        principalTable: "cotizaciones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_cotizacion_lineas_partida",
                schema: "compras",
                table: "cotizacion_lineas",
                columns: new[] { "cotizacion_id", "linea_requisicion_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cotizaciones_orden_compra",
                schema: "compras",
                table: "cotizaciones",
                column: "orden_compra_id",
                filter: "orden_compra_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_cotizaciones_requisicion",
                schema: "compras",
                table: "cotizaciones",
                column: "requisicion_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cotizacion_lineas",
                schema: "compras");

            migrationBuilder.DropTable(
                name: "cotizaciones",
                schema: "compras");
        }
    }
}
