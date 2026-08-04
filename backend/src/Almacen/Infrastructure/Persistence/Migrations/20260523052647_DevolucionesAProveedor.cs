using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Almacen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DevolucionesAProveedor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "devoluciones_proveedor",
                schema: "almacen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    proveedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recepcion_origen_id = table.Column<Guid>(type: "uuid", nullable: true),
                    factura_proveedor_origen_id = table.Column<Guid>(type: "uuid", nullable: true),
                    orden_compra_origen_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sub_almacen_origen_id = table.Column<Guid>(type: "uuid", nullable: true),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    motivo = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    solicitada_por = table.Column<Guid>(type: "uuid", nullable: false),
                    solicitada_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    autorizada_por = table.Column<Guid>(type: "uuid", nullable: true),
                    autorizada_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    motivo_rechazo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    rechazada_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    movimiento_salida_id = table.Column<Guid>(type: "uuid", nullable: true),
                    folio_movimiento_salida = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    registrada_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    nota_credito_fiscal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    conciliada_con_nc_fiscal_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_devoluciones_proveedor", x => x.id);
                    table.CheckConstraint("ck_devoluciones_prov_conciliada_tiene_nc", "(estado = 4 AND nota_credito_fiscal_id IS NOT NULL) OR (estado <> 4)");
                    table.CheckConstraint("ck_devoluciones_prov_estado", "estado BETWEEN 0 AND 5");
                    table.CheckConstraint("ck_devoluciones_prov_rechazada_tiene_motivo", "(estado = 5 AND motivo_rechazo IS NOT NULL) OR (estado <> 5)");
                    table.CheckConstraint("ck_devoluciones_prov_registrada_tiene_movimiento", "(estado = 3 AND movimiento_salida_id IS NOT NULL) OR (estado <> 3 AND estado <> 4)");
                });

            migrationBuilder.CreateTable(
                name: "evidencias_devolucion_proveedor",
                schema: "almacen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    devolucion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_evidencia = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    nombre_archivo = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    blob_ref = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    comentario = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_evidencias_devolucion_proveedor", x => x.id);
                    table.ForeignKey(
                        name: "fk_evidencias_devolucion_proveedor_devoluciones_proveedor_devo",
                        column: x => x.devolucion_id,
                        principalSchema: "almacen",
                        principalTable: "devoluciones_proveedor",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "lineas_devolucion_proveedor",
                schema: "almacen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    devolucion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    posicion = table.Column<int>(type: "integer", nullable: false),
                    articulo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cantidad = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    unidad_medida = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    costo_unitario_mxn = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    monto_total_mxn = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    linea_recepcion_origen_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lineas_devolucion_proveedor", x => x.id);
                    table.CheckConstraint("ck_lineas_dev_prov_cantidad_positiva", "cantidad > 0");
                    table.CheckConstraint("ck_lineas_dev_prov_costo_no_negativo", "costo_unitario_mxn >= 0");
                    table.ForeignKey(
                        name: "fk_lineas_devolucion_proveedor_devoluciones_proveedor_devoluci",
                        column: x => x.devolucion_id,
                        principalSchema: "almacen",
                        principalTable: "devoluciones_proveedor",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_devoluciones_prov_estado",
                schema: "almacen",
                table: "devoluciones_proveedor",
                column: "estado");

            migrationBuilder.CreateIndex(
                name: "ix_devoluciones_prov_pendientes_nc_fiscal",
                schema: "almacen",
                table: "devoluciones_proveedor",
                column: "proveedor_id",
                filter: "estado = 3");

            migrationBuilder.CreateIndex(
                name: "ix_evidencias_devolucion_proveedor_devolucion_id",
                schema: "almacen",
                table: "evidencias_devolucion_proveedor",
                column: "devolucion_id");

            migrationBuilder.CreateIndex(
                name: "ix_lineas_devolucion_proveedor_articulo_id",
                schema: "almacen",
                table: "lineas_devolucion_proveedor",
                column: "articulo_id");

            migrationBuilder.CreateIndex(
                name: "ix_lineas_devolucion_proveedor_devolucion_id",
                schema: "almacen",
                table: "lineas_devolucion_proveedor",
                column: "devolucion_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "evidencias_devolucion_proveedor",
                schema: "almacen");

            migrationBuilder.DropTable(
                name: "lineas_devolucion_proveedor",
                schema: "almacen");

            migrationBuilder.DropTable(
                name: "devoluciones_proveedor",
                schema: "almacen");
        }
    }
}
