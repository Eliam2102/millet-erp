using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FacturasProveedor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "facturas_proveedor",
                schema: "cuentas_por_pagar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cfdi_recibido_id = table.Column<Guid>(type: "uuid", nullable: true),
                    uuid_cfdi = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    proveedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    folio_proveedor = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    serie_proveedor = table.Column<string>(type: "character varying(25)", maxLength: 25, nullable: true),
                    fecha_documento = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fecha_contabilizacion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fecha_vencimiento = table.Column<DateOnly>(type: "date", nullable: false),
                    moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    tipo_cambio = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    subtotal = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    descuentos = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    impuestos_trasladados = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    retenciones = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    orden_compra_id = table.Column<Guid>(type: "uuid", nullable: true),
                    encargado_compras_snapshot = table.Column<Guid>(type: "uuid", nullable: true),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    tolerancia_tipo = table.Column<short>(type: "smallint", nullable: true),
                    tolerancia_valor = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    diferencia_contra_oc = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    redondeo_aplicado = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    anticipo_aplicado_total = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    nc_aplicadas_total = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    importe_pagado = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    motivo_cancelacion = table.Column<short>(type: "smallint", nullable: true),
                    motivo_cancelacion_texto = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    fecha_cancelacion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    en_revision = table.Column<bool>(type: "boolean", nullable: false),
                    motivo_revision_id = table.Column<Guid>(type: "uuid", nullable: true),
                    dependencia_revisora_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fecha_entrada_revision = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_facturas_proveedor", x => x.id);
                    table.CheckConstraint("ck_facturas_proveedor_saldo_no_negativo", "total - anticipo_aplicado_total - nc_aplicadas_total - importe_pagado >= 0");
                    table.CheckConstraint("ck_facturas_proveedor_total_positivo", "total > 0");
                });

            migrationBuilder.CreateTable(
                name: "bitacora_estado_factura",
                schema: "cuentas_por_pagar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    factura_proveedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    estado_anterior = table.Column<short>(type: "smallint", nullable: false),
                    estado_nuevo = table.Column<short>(type: "smallint", nullable: false),
                    ocurrido_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    motivo = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bitacora_estado_factura", x => x.id);
                    table.ForeignKey(
                        name: "fk_bitacora_estado_factura_facturas_proveedor_factura_proveedo",
                        column: x => x.factura_proveedor_id,
                        principalSchema: "cuentas_por_pagar",
                        principalTable: "facturas_proveedor",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "lineas_factura_proveedor",
                schema: "cuentas_por_pagar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    factura_proveedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    posicion = table.Column<int>(type: "integer", nullable: false),
                    articulo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    clave_prod_serv = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    descripcion = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    cantidad = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    clave_unidad = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    unidad = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    precio_unitario = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    importe = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    descuento = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    linea_oc_id = table.Column<Guid>(type: "uuid", nullable: true),
                    concepto_contable_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lineas_factura_proveedor", x => x.id);
                    table.ForeignKey(
                        name: "fk_lineas_factura_proveedor_facturas_proveedor_factura_proveed",
                        column: x => x.factura_proveedor_id,
                        principalSchema: "cuentas_por_pagar",
                        principalTable: "facturas_proveedor",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bitacora_estado_factura_factura_fecha",
                schema: "cuentas_por_pagar",
                table: "bitacora_estado_factura",
                columns: new[] { "factura_proveedor_id", "ocurrido_en" });

            migrationBuilder.CreateIndex(
                name: "ix_facturas_proveedor_bandeja",
                schema: "cuentas_por_pagar",
                table: "facturas_proveedor",
                columns: new[] { "empresa_id", "estado", "fecha_documento" });

            migrationBuilder.CreateIndex(
                name: "ix_facturas_proveedor_saldo",
                schema: "cuentas_por_pagar",
                table: "facturas_proveedor",
                columns: new[] { "proveedor_id", "fecha_vencimiento" },
                filter: "estado IN (1, 3) AND (total - anticipo_aplicado_total - nc_aplicadas_total - importe_pagado) > 0");

            migrationBuilder.CreateIndex(
                name: "ix_facturas_proveedor_uuid",
                schema: "cuentas_por_pagar",
                table: "facturas_proveedor",
                column: "uuid_cfdi",
                filter: "uuid_cfdi IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_facturas_revision_dependencia",
                schema: "cuentas_por_pagar",
                table: "facturas_proveedor",
                columns: new[] { "dependencia_revisora_id", "fecha_entrada_revision" },
                filter: "en_revision = true");

            migrationBuilder.CreateIndex(
                name: "ux_lineas_factura_proveedor_pos",
                schema: "cuentas_por_pagar",
                table: "lineas_factura_proveedor",
                columns: new[] { "factura_proveedor_id", "posicion" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bitacora_estado_factura",
                schema: "cuentas_por_pagar");

            migrationBuilder.DropTable(
                name: "lineas_factura_proveedor",
                schema: "cuentas_por_pagar");

            migrationBuilder.DropTable(
                name: "facturas_proveedor",
                schema: "cuentas_por_pagar");
        }
    }
}
