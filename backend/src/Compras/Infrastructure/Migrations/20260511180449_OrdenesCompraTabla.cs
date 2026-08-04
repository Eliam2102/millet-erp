using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OrdenesCompraTabla : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "folio_secuencias_oc",
                schema: "compras",
                columns: table => new
                {
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    anio = table.Column<short>(type: "smallint", nullable: false),
                    siguiente = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_folio_secuencias_oc", x => new { x.empresa_id, x.sucursal_id, x.anio });
                });

            migrationBuilder.CreateTable(
                name: "ordenes_compra",
                schema: "compras",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    folio = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    folio_anio = table.Column<short>(type: "smallint", nullable: false),
                    proveedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sucursal_destino_id = table.Column<Guid>(type: "uuid", nullable: false),
                    almacen_destino_default_id = table.Column<Guid>(type: "uuid", nullable: false),
                    condiciones_pago_id = table.Column<Guid>(type: "uuid", nullable: false),
                    uso_principal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    moneda = table.Column<string>(type: "char(3)", nullable: false, defaultValue: "MXN"),
                    tipo_cambio = table.Column<decimal>(type: "numeric(18,6)", nullable: true),
                    comprador_titular_id = table.Column<Guid>(type: "uuid", nullable: false),
                    encargado_compras_id = table.Column<Guid>(type: "uuid", nullable: false),
                    observaciones = table.Column<string>(type: "text", nullable: true),
                    sin_requisicion_previa = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    es_importacion = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    cotizacion_excepcionada = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    fecha_documento = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fecha_contabilizacion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    fecha_entrega_esperada = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    sub_estado_recepcion = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0),
                    sub_estado_facturacion = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0),
                    sub_estado_pago = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0),
                    motivo_sin_requisicion = table.Column<string>(type: "text", nullable: true),
                    motivo_cancelacion = table.Column<string>(type: "text", nullable: true),
                    motivo_rechazo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    motivo_rechazo_texto = table.Column<string>(type: "text", nullable: true),
                    oc_origen_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contacto_proveedor_email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    contacto_proveedor_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    contacto_proveedor_telefono = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    descuento_global_tipo = table.Column<short>(type: "smallint", nullable: true),
                    descuento_global_valor = table.Column<decimal>(type: "numeric(15,4)", nullable: true),
                    gastos_adicionales = table.Column<decimal>(type: "numeric(15,2)", nullable: false, defaultValue: 0m),
                    info_import_codigo_ruta = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    info_import_incoterm_id = table.Column<Guid>(type: "uuid", nullable: true),
                    info_import_numero_contenedor = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    info_import_numero_pedimento = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    info_import_pais_origen = table.Column<string>(type: "char(2)", nullable: true),
                    info_import_semana_embarque = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    info_logistica_direccion = table.Column<string>(type: "text", nullable: true),
                    info_logistica_instrucciones = table.Column<string>(type: "text", nullable: true),
                    info_logistica_numero_guia = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    info_logistica_transportista_id = table.Column<Guid>(type: "uuid", nullable: true),
                    info_logistica_transportista_texto = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    redondeo = table.Column<decimal>(type: "numeric(15,2)", nullable: false, defaultValue: 0m),
                    referencia_proveedor = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ordenes_compra", x => x.id);
                    table.CheckConstraint("ck_oc_estado", "estado BETWEEN 0 AND 6");
                    table.CheckConstraint("ck_oc_import_campos", "(es_importacion = false) OR (es_importacion = true AND info_import_incoterm_id IS NOT NULL AND info_import_pais_origen IS NOT NULL AND info_import_numero_contenedor IS NOT NULL)");
                    table.CheckConstraint("ck_oc_sin_rq_motivo", "sin_requisicion_previa = false OR motivo_sin_requisicion IS NOT NULL");
                    table.CheckConstraint("ck_oc_sub_facturacion", "sub_estado_facturacion BETWEEN 0 AND 2");
                    table.CheckConstraint("ck_oc_sub_pago", "sub_estado_pago BETWEEN 0 AND 2");
                    table.CheckConstraint("ck_oc_sub_recepcion", "sub_estado_recepcion BETWEEN 0 AND 2");
                    table.CheckConstraint("ck_oc_tipo_cambio", "(moneda = 'MXN' AND tipo_cambio IS NULL) OR (moneda <> 'MXN' AND tipo_cambio > 0)");
                    table.ForeignKey(
                        name: "fk_oc_origen",
                        column: x => x.oc_origen_id,
                        principalSchema: "compras",
                        principalTable: "ordenes_compra",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_oc_contenedor",
                schema: "compras",
                table: "ordenes_compra",
                column: "info_import_numero_contenedor",
                filter: "info_import_numero_contenedor IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_oc_empresa_comprador",
                schema: "compras",
                table: "ordenes_compra",
                columns: new[] { "empresa_id", "comprador_titular_id" });

            migrationBuilder.CreateIndex(
                name: "ix_oc_empresa_estado",
                schema: "compras",
                table: "ordenes_compra",
                columns: new[] { "empresa_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_oc_empresa_fecha",
                schema: "compras",
                table: "ordenes_compra",
                columns: new[] { "empresa_id", "fecha_documento" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_oc_empresa_proveedor",
                schema: "compras",
                table: "ordenes_compra",
                columns: new[] { "empresa_id", "proveedor_id" });

            migrationBuilder.CreateIndex(
                name: "ix_oc_origen",
                schema: "compras",
                table: "ordenes_compra",
                column: "oc_origen_id",
                filter: "oc_origen_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_oc_partidas_abiertas",
                schema: "compras",
                table: "ordenes_compra",
                columns: new[] { "empresa_id", "estado", "sub_estado_recepcion", "sub_estado_facturacion", "sub_estado_pago" },
                filter: "estado = 3");

            migrationBuilder.CreateIndex(
                name: "ix_oc_referencia_proveedor",
                schema: "compras",
                table: "ordenes_compra",
                columns: new[] { "empresa_id", "referencia_proveedor" },
                filter: "referencia_proveedor IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_oc_ruta",
                schema: "compras",
                table: "ordenes_compra",
                column: "info_import_codigo_ruta",
                filter: "info_import_codigo_ruta IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_oc_semana",
                schema: "compras",
                table: "ordenes_compra",
                column: "info_import_semana_embarque",
                filter: "info_import_semana_embarque IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "uq_ordenes_compra_folio",
                schema: "compras",
                table: "ordenes_compra",
                columns: new[] { "empresa_id", "sucursal_destino_id", "folio" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "folio_secuencias_oc",
                schema: "compras");

            migrationBuilder.DropTable(
                name: "ordenes_compra",
                schema: "compras");
        }
    }
}
