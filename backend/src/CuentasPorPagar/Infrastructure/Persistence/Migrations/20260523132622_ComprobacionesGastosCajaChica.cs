using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ComprobacionesGastosCajaChica : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "comprobaciones_gastos",
                schema: "cuentas_por_pagar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<short>(type: "smallint", nullable: false),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    responsable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha_inicio = table.Column<DateOnly>(type: "date", nullable: false),
                    fecha_fin = table.Column<DateOnly>(type: "date", nullable: false),
                    moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    monto_total = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    autorizado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    aplicado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    rechazado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    fecha_creacion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fecha_envio_revision = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    fecha_autorizacion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    fecha_aplicacion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    fecha_rechazo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    motivo_rechazo = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    observaciones = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_comprobaciones_gastos", x => x.id);
                    table.CheckConstraint("ck_comprobacion_monto_no_negativo", "monto_total >= 0");
                });

            migrationBuilder.CreateTable(
                name: "lineas_comprobacion_gastos",
                schema: "cuentas_por_pagar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    comprobacion_gastos_id = table.Column<Guid>(type: "uuid", nullable: false),
                    factura_proveedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cfdi_recibido_id = table.Column<Guid>(type: "uuid", nullable: true),
                    uuid_cfdi = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    proveedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    folio_proveedor = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    fecha_cfdi = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    subtotal = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    impuestos_trasladados = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    retenciones = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    concepto = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lineas_comprobacion_gastos", x => x.id);
                    table.CheckConstraint("ck_linea_comp_total_positivo", "total > 0");
                    table.ForeignKey(
                        name: "fk_lineas_comprobacion_gastos_comprobaciones_gastos_comprobaci",
                        column: x => x.comprobacion_gastos_id,
                        principalSchema: "cuentas_por_pagar",
                        principalTable: "comprobaciones_gastos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_comprobaciones_responsable_estado",
                schema: "cuentas_por_pagar",
                table: "comprobaciones_gastos",
                columns: new[] { "responsable_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_comprobaciones_tipo_estado_fecha",
                schema: "cuentas_por_pagar",
                table: "comprobaciones_gastos",
                columns: new[] { "tipo", "estado", "fecha_creacion" });

            migrationBuilder.CreateIndex(
                name: "ix_lineas_comprobacion_gastos_comprobacion_gastos_id",
                schema: "cuentas_por_pagar",
                table: "lineas_comprobacion_gastos",
                column: "comprobacion_gastos_id");

            migrationBuilder.CreateIndex(
                name: "ux_linea_comp_factura",
                schema: "cuentas_por_pagar",
                table: "lineas_comprobacion_gastos",
                column: "factura_proveedor_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lineas_comprobacion_gastos",
                schema: "cuentas_por_pagar");

            migrationBuilder.DropTable(
                name: "comprobaciones_gastos",
                schema: "cuentas_por_pagar");
        }
    }
}
