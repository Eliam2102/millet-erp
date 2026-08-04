using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ComprobantesYFacturaVenta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "comprobante",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<short>(type: "smallint", nullable: false),
                    folio = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    folio_numero = table.Column<long>(type: "bigint", nullable: false),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    caja_id = table.Column<Guid>(type: "uuid", nullable: true),
                    usuario_emisor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    receptor_rfc = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    receptor_nombre = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    receptor_regimen_fiscal = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    receptor_codigo_postal = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    receptor_uso_cfdi = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    receptor_pais = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    receptor_es_generico = table.Column<bool>(type: "boolean", nullable: false),
                    rfc_emisor = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    regimen_fiscal_emisor = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    metodo_pago = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    forma_pago = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    tipo_cambio = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    descuento = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    impuestos_trasladados = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    retenciones = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    periodo_anio = table.Column<int>(type: "integer", nullable: false),
                    periodo_mes = table.Column<int>(type: "integer", nullable: false),
                    uuid = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    sello_cfdi = table.Column<string>(type: "text", nullable: true),
                    sello_sat = table.Column<string>(type: "text", nullable: true),
                    no_certificado_sat = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    fecha_timbrado = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rfc_proveedor_certificacion = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: true),
                    cfdi_archivo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    timbrado_error_codigo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    timbrado_error_mensaje = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    enviado_correo = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_comprobante", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "factura_venta",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    canal_venta = table.Column<short>(type: "smallint", nullable: false),
                    comportamiento_fiscal = table.Column<short>(type: "smallint", nullable: false),
                    pedido_facturable_id = table.Column<Guid>(type: "uuid", nullable: true),
                    obra_id = table.Column<long>(type: "bigint", nullable: true),
                    obra_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    factura_agrupada = table.Column<bool>(type: "boolean", nullable: false),
                    autorizacion_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_factura_venta", x => x.id);
                    table.ForeignKey(
                        name: "fk_factura_venta_comprobante_id",
                        column: x => x.id,
                        principalSchema: "facturacion",
                        principalTable: "comprobante",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "factura_venta_linea",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    factura_venta_id = table.Column<Guid>(type: "uuid", nullable: false),
                    posicion = table.Column<int>(type: "integer", nullable: false),
                    producto_id = table.Column<Guid>(type: "uuid", nullable: true),
                    clave_prod_serv_sat = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    clave_unidad_sat = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    cantidad = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    valor_unitario = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    descuento = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    importe = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    objeto_imp = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    tasa_iva_traslado = table.Column<decimal>(type: "numeric(8,6)", precision: 8, scale: 6, nullable: true),
                    impuesto_trasladado_importe = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    tasa_retencion_iva = table.Column<decimal>(type: "numeric(8,6)", precision: 8, scale: 6, nullable: true),
                    tasa_retencion_isr = table.Column<decimal>(type: "numeric(8,6)", precision: 8, scale: 6, nullable: true),
                    retencion_total_importe = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_factura_venta_linea", x => x.id);
                    table.ForeignKey(
                        name: "fk_factura_venta_linea_facturas_venta_factura_venta_id",
                        column: x => x.factura_venta_id,
                        principalSchema: "facturacion",
                        principalTable: "factura_venta",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_comprobante_estado",
                schema: "facturacion",
                table: "comprobante",
                column: "estado");

            migrationBuilder.CreateIndex(
                name: "ix_comprobante_periodo",
                schema: "facturacion",
                table: "comprobante",
                columns: new[] { "empresa_id", "periodo_anio", "periodo_mes" });

            migrationBuilder.CreateIndex(
                name: "ix_comprobante_sucursal_folio",
                schema: "facturacion",
                table: "comprobante",
                columns: new[] { "sucursal_id", "folio" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_comprobante_uuid",
                schema: "facturacion",
                table: "comprobante",
                column: "uuid",
                unique: true,
                filter: "uuid IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_factura_venta_obra",
                schema: "facturacion",
                table: "factura_venta",
                column: "obra_id",
                filter: "obra_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_factura_venta_pedido",
                schema: "facturacion",
                table: "factura_venta",
                column: "pedido_facturable_id",
                filter: "pedido_facturable_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_factura_venta_linea_posicion",
                schema: "facturacion",
                table: "factura_venta_linea",
                columns: new[] { "factura_venta_id", "posicion" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "factura_venta_linea",
                schema: "facturacion");

            migrationBuilder.DropTable(
                name: "factura_venta",
                schema: "facturacion");

            migrationBuilder.DropTable(
                name: "comprobante",
                schema: "facturacion");
        }
    }
}
