using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RespuestasProveedorOc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "estado_comercial_proveedor",
                schema: "compras",
                table: "ordenes_compra",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<DateOnly>(
                name: "fecha_compromiso_proveedor",
                schema: "compras",
                table: "ordenes_compra",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "oc_respuestas_proveedor",
                schema: "compras",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    orden_compra_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<int>(type: "integer", nullable: false),
                    tipo = table.Column<short>(type: "smallint", nullable: false),
                    fecha_respuesta = table.Column<DateOnly>(type: "date", nullable: false),
                    evidencia = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    contacto_proveedor = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    tiene_diferencias = table.Column<bool>(type: "boolean", nullable: false),
                    registrada_por_id = table.Column<Guid>(type: "uuid", nullable: false),
                    registrada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_oc_respuestas_proveedor", x => x.id);
                    table.CheckConstraint("ck_oc_respuestas_proveedor_tipo", "tipo IN (1, 2)");
                    table.ForeignKey(
                        name: "fk_oc_respuestas_proveedor_ordenes_compra_orden_compra_id",
                        column: x => x.orden_compra_id,
                        principalSchema: "compras",
                        principalTable: "ordenes_compra",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "oc_compromisos_entrega",
                schema: "compras",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    respuesta_proveedor_oc_id = table.Column<Guid>(type: "uuid", nullable: false),
                    orden_compra_id = table.Column<Guid>(type: "uuid", nullable: false),
                    linea_orden_compra_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cantidad = table.Column<decimal>(type: "numeric(15,4)", nullable: false),
                    fecha_prometida = table.Column<DateOnly>(type: "date", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_oc_compromisos_entrega", x => x.id);
                    table.CheckConstraint("ck_oc_compromisos_entrega_cantidad", "cantidad > 0");
                    table.ForeignKey(
                        name: "fk_oc_compromisos_entrega_oc_respuestas_proveedor_respuesta_pr",
                        column: x => x.respuesta_proveedor_oc_id,
                        principalSchema: "compras",
                        principalTable: "oc_respuestas_proveedor",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_oc_compromisos_entrega_orden_compra_lineas_linea_orden_comp",
                        column: x => x.linea_orden_compra_id,
                        principalSchema: "compras",
                        principalTable: "orden_compra_lineas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_oc_estado_comercial_proveedor",
                schema: "compras",
                table: "ordenes_compra",
                sql: "estado_comercial_proveedor BETWEEN 0 AND 3");

            migrationBuilder.CreateIndex(
                name: "ix_oc_compromisos_entrega_linea_orden_compra_id",
                schema: "compras",
                table: "oc_compromisos_entrega",
                column: "linea_orden_compra_id");

            migrationBuilder.CreateIndex(
                name: "ix_oc_compromisos_entrega_respuesta_proveedor_oc_id",
                schema: "compras",
                table: "oc_compromisos_entrega",
                column: "respuesta_proveedor_oc_id");

            migrationBuilder.CreateIndex(
                name: "ux_oc_respuestas_proveedor_numero",
                schema: "compras",
                table: "oc_respuestas_proveedor",
                columns: new[] { "orden_compra_id", "numero" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "oc_compromisos_entrega",
                schema: "compras");

            migrationBuilder.DropTable(
                name: "oc_respuestas_proveedor",
                schema: "compras");

            migrationBuilder.DropCheckConstraint(
                name: "ck_oc_estado_comercial_proveedor",
                schema: "compras",
                table: "ordenes_compra");

            migrationBuilder.DropColumn(
                name: "estado_comercial_proveedor",
                schema: "compras",
                table: "ordenes_compra");

            migrationBuilder.DropColumn(
                name: "fecha_compromiso_proveedor",
                schema: "compras",
                table: "ordenes_compra");
        }
    }
}
