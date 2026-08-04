using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LineasYTotalesOc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "orden_compra_lineas",
                schema: "compras",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    orden_compra_id = table.Column<Guid>(type: "uuid", nullable: false),
                    posicion = table.Column<int>(type: "integer", nullable: false),
                    articulo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    descripcion_extendida = table.Column<string>(type: "text", nullable: true),
                    cantidad = table.Column<decimal>(type: "numeric(15,4)", nullable: false),
                    unidad_medida = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    precio_unitario = table.Column<decimal>(type: "numeric(15,4)", nullable: false),
                    indicador_impuestos = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    iva_importe = table.Column<decimal>(type: "numeric(15,2)", nullable: false, defaultValue: 0m),
                    retencion_isr = table.Column<decimal>(type: "numeric(15,2)", nullable: true),
                    almacen_destino_id = table.Column<Guid>(type: "uuid", nullable: false),
                    departamento_solicitante_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requisicion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    linea_requisicion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fecha_entrega_linea = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cantidad_recibida = table.Column<decimal>(type: "numeric(15,4)", nullable: false, defaultValue: 0m),
                    cantidad_facturada = table.Column<decimal>(type: "numeric(15,4)", nullable: false, defaultValue: 0m),
                    texto_adicional = table.Column<string>(type: "text", nullable: true),
                    descuento_tipo = table.Column<short>(type: "smallint", nullable: false),
                    descuento_valor = table.Column<decimal>(type: "numeric(15,4)", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_orden_compra_lineas", x => x.id);
                    table.CheckConstraint("ck_oc_lineas_cantidad_pos", "cantidad > 0");
                    table.CheckConstraint("ck_oc_lineas_descuento_tipo", "descuento_tipo BETWEEN 0 AND 1");
                    table.CheckConstraint("ck_oc_lineas_facturada_max", "cantidad_facturada <= cantidad");
                    table.CheckConstraint("ck_oc_lineas_facturada_nn", "cantidad_facturada >= 0");
                    table.CheckConstraint("ck_oc_lineas_precio_nn", "precio_unitario >= 0");
                    table.CheckConstraint("ck_oc_lineas_recibida_max", "cantidad_recibida <= cantidad");
                    table.CheckConstraint("ck_oc_lineas_recibida_nn", "cantidad_recibida >= 0");
                    table.CheckConstraint("ck_oc_lineas_rq_coherente", "(requisicion_id IS NULL AND linea_requisicion_id IS NULL) OR (requisicion_id IS NOT NULL AND linea_requisicion_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_orden_compra_lineas_ordenes_compra_orden_compra_id",
                        column: x => x.orden_compra_id,
                        principalSchema: "compras",
                        principalTable: "ordenes_compra",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_oc_lineas_almacen",
                schema: "compras",
                table: "orden_compra_lineas",
                column: "almacen_destino_id");

            migrationBuilder.CreateIndex(
                name: "ix_oc_lineas_articulo",
                schema: "compras",
                table: "orden_compra_lineas",
                column: "articulo_id");

            migrationBuilder.CreateIndex(
                name: "ix_oc_lineas_oc",
                schema: "compras",
                table: "orden_compra_lineas",
                column: "orden_compra_id");

            migrationBuilder.CreateIndex(
                name: "ix_oc_lineas_rq",
                schema: "compras",
                table: "orden_compra_lineas",
                column: "requisicion_id",
                filter: "requisicion_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "uq_oc_lineas_posicion",
                schema: "compras",
                table: "orden_compra_lineas",
                columns: new[] { "orden_compra_id", "posicion" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "orden_compra_lineas",
                schema: "compras");
        }
    }
}
