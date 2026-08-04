using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorCobrar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Cartera : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "eventos_procesados",
                schema: "cuentas_por_cobrar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    evento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evento_tipo = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    procesado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    detalle = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_eventos_procesados", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "factura_cartera",
                schema: "cuentas_por_cobrar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    factura_venta_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: true),
                    receptor_rfc = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    receptor_nombre = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    uuid = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    folio = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    total = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    metodo_pago = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    fecha_timbrado = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fecha_vencimiento = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    monto_pagado = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    monto_nc = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_factura_cartera", x => x.id);
                    table.CheckConstraint("ck_factura_cartera_acumulados", "monto_pagado >= 0 AND monto_nc >= 0 AND monto_pagado + monto_nc <= total");
                    table.CheckConstraint("ck_factura_cartera_estado", "estado IN (1, 2, 3, 4)");
                    table.CheckConstraint("ck_factura_cartera_total_positivo", "total > 0");
                });

            migrationBuilder.CreateTable(
                name: "movimiento_cartera",
                schema: "cuentas_por_cobrar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    factura_cartera_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<short>(type: "smallint", nullable: false),
                    origen_comprobante_id = table.Column<Guid>(type: "uuid", nullable: false),
                    importe = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    fecha_movimiento = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revertido = table.Column<bool>(type: "boolean", nullable: false),
                    revertido_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_movimiento_cartera", x => x.id);
                    table.CheckConstraint("ck_movimiento_cartera_importe_positivo", "importe > 0");
                    table.CheckConstraint("ck_movimiento_cartera_reversa_consistente", "(revertido = TRUE AND revertido_en IS NOT NULL) OR (revertido = FALSE)");
                    table.CheckConstraint("ck_movimiento_cartera_tipo", "tipo IN (1, 2)");
                    table.ForeignKey(
                        name: "fk_movimiento_cartera_factura_cartera_factura_cartera_id",
                        column: x => x.factura_cartera_id,
                        principalSchema: "cuentas_por_cobrar",
                        principalTable: "factura_cartera",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_eventos_procesados_id_tipo",
                schema: "cuentas_por_cobrar",
                table: "eventos_procesados",
                columns: new[] { "evento_id", "evento_tipo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_factura_cartera_cliente_estado",
                schema: "cuentas_por_cobrar",
                table: "factura_cartera",
                columns: new[] { "cliente_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_factura_cartera_receptor_rfc",
                schema: "cuentas_por_cobrar",
                table: "factura_cartera",
                column: "receptor_rfc");

            migrationBuilder.CreateIndex(
                name: "ix_factura_cartera_vencimiento_abiertas",
                schema: "cuentas_por_cobrar",
                table: "factura_cartera",
                column: "fecha_vencimiento",
                filter: "estado IN (1, 2)");

            migrationBuilder.CreateIndex(
                name: "ux_factura_cartera_factura_venta",
                schema: "cuentas_por_cobrar",
                table: "factura_cartera",
                column: "factura_venta_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_movimiento_cartera_factura",
                schema: "cuentas_por_cobrar",
                table: "movimiento_cartera",
                column: "factura_cartera_id");

            migrationBuilder.CreateIndex(
                name: "ix_movimiento_cartera_origen",
                schema: "cuentas_por_cobrar",
                table: "movimiento_cartera",
                column: "origen_comprobante_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "eventos_procesados",
                schema: "cuentas_por_cobrar");

            migrationBuilder.DropTable(
                name: "movimiento_cartera",
                schema: "cuentas_por_cobrar");

            migrationBuilder.DropTable(
                name: "factura_cartera",
                schema: "cuentas_por_cobrar");
        }
    }
}
