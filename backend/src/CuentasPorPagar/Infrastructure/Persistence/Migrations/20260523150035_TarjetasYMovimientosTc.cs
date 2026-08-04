using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TarjetasYMovimientosTc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "movimientos_tarjeta_credito",
                schema: "cuentas_por_pagar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tarjeta_id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_que_uso_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha_movimiento = table.Column<DateOnly>(type: "date", nullable: false),
                    fecha_aplicacion_banco = table.Column<DateOnly>(type: "date", nullable: true),
                    tipo = table.Column<short>(type: "smallint", nullable: false),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    monto_original = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    moneda_original = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    tipo_cambio_captura = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: true),
                    monto_mxn = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    merchant_raw = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    merchant_normalizado = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    descripcion_libre = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    cfdi_recibido_id = table.Column<Guid>(type: "uuid", nullable: true),
                    factura_proveedor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    proveedor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    concepto_contable = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    movimiento_original_id = table.Column<Guid>(type: "uuid", nullable: true),
                    estado_cuenta_tc_id = table.Column<Guid>(type: "uuid", nullable: true),
                    estado_cuenta_tc_linea_id = table.Column<Guid>(type: "uuid", nullable: true),
                    captura_retroactiva = table.Column<bool>(type: "boolean", nullable: false),
                    ticket_blob_ref = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    en_disputa = table.Column<bool>(type: "boolean", nullable: false),
                    motivo_disputa = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    fecha_inicio_disputa = table.Column<DateOnly>(type: "date", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_movimientos_tarjeta_credito", x => x.id);
                    table.CheckConstraint("ck_mov_tc_cfdi_consistente", "(tipo = 1 AND cfdi_recibido_id IS NOT NULL AND factura_proveedor_id IS NOT NULL) OR (tipo != 1 AND cfdi_recibido_id IS NULL)");
                    table.CheckConstraint("ck_mov_tc_moneda_consistente", "(moneda_original = 'MXN' AND tipo_cambio_captura IS NULL) OR (moneda_original != 'MXN' AND tipo_cambio_captura IS NOT NULL)");
                    table.CheckConstraint("ck_mov_tc_monto_positivo", "monto_original > 0");
                    table.CheckConstraint("ck_mov_tc_refund_consistente", "(tipo = 3 AND movimiento_original_id IS NOT NULL) OR (tipo != 3)");
                });

            migrationBuilder.CreateTable(
                name: "tarjetas_credito",
                schema: "cuentas_por_pagar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    emisora = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    perfil_parser = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    numero_enmascarado = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    nombre_alias = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    titular_id = table.Column<Guid>(type: "uuid", nullable: false),
                    banco_proveedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    limite_credito_mxn = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    moneda_default = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    dia_corte = table.Column<short>(type: "smallint", nullable: false),
                    dia_limite_pago = table.Column<short>(type: "smallint", nullable: false),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    fecha_bloqueo = table.Column<DateOnly>(type: "date", nullable: true),
                    motivo_bloqueo = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    vigencia_desde = table.Column<DateOnly>(type: "date", nullable: false),
                    vigencia_hasta = table.Column<DateOnly>(type: "date", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tarjetas_credito", x => x.id);
                    table.CheckConstraint("ck_tarjeta_bloqueo_consistente", "(estado = 2 AND fecha_bloqueo IS NOT NULL) OR (estado != 2)");
                    table.CheckConstraint("ck_tarjeta_dia_corte", "dia_corte BETWEEN 1 AND 31");
                    table.CheckConstraint("ck_tarjeta_limite_positivo", "limite_credito_mxn > 0");
                });

            migrationBuilder.CreateTable(
                name: "tarjeta_usuarios_autorizados",
                schema: "cuentas_por_pagar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tarjeta_id = table.Column<Guid>(type: "uuid", nullable: false),
                    empleado_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vigencia_desde = table.Column<DateOnly>(type: "date", nullable: false),
                    vigencia_hasta = table.Column<DateOnly>(type: "date", nullable: true),
                    monto_max_mensual_mxn = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tarjeta_usuarios_autorizados", x => x.id);
                    table.ForeignKey(
                        name: "fk_tarjeta_usuarios_autorizados_tarjetas_credito_tarjeta_id",
                        column: x => x.tarjeta_id,
                        principalSchema: "cuentas_por_pagar",
                        principalTable: "tarjetas_credito",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mov_tc_merchant_norm",
                schema: "cuentas_por_pagar",
                table: "movimientos_tarjeta_credito",
                column: "merchant_normalizado");

            migrationBuilder.CreateIndex(
                name: "ix_mov_tc_tarjeta_estado_cuenta",
                schema: "cuentas_por_pagar",
                table: "movimientos_tarjeta_credito",
                columns: new[] { "tarjeta_id", "estado_cuenta_tc_id" },
                filter: "estado_cuenta_tc_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_mov_tc_tarjeta_fecha_estado",
                schema: "cuentas_por_pagar",
                table: "movimientos_tarjeta_credito",
                columns: new[] { "tarjeta_id", "fecha_movimiento", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_mov_tc_usuario",
                schema: "cuentas_por_pagar",
                table: "movimientos_tarjeta_credito",
                column: "usuario_que_uso_id");

            migrationBuilder.CreateIndex(
                name: "ix_tc_usuarios_tarjeta_empleado",
                schema: "cuentas_por_pagar",
                table: "tarjeta_usuarios_autorizados",
                columns: new[] { "tarjeta_id", "empleado_id", "vigencia_desde" });

            migrationBuilder.CreateIndex(
                name: "ix_tarjetas_banco",
                schema: "cuentas_por_pagar",
                table: "tarjetas_credito",
                column: "banco_proveedor_id");

            migrationBuilder.CreateIndex(
                name: "ix_tarjetas_titular_estado",
                schema: "cuentas_por_pagar",
                table: "tarjetas_credito",
                columns: new[] { "titular_id", "estado" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "movimientos_tarjeta_credito",
                schema: "cuentas_por_pagar");

            migrationBuilder.DropTable(
                name: "tarjeta_usuarios_autorizados",
                schema: "cuentas_por_pagar");

            migrationBuilder.DropTable(
                name: "tarjetas_credito",
                schema: "cuentas_por_pagar");
        }
    }
}
