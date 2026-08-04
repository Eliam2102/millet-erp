using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ViaticosYCatalogosCxp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "aprobadores_limites",
                schema: "cuentas_por_pagar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    empleado_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_gasto = table.Column<short>(type: "smallint", nullable: false),
                    monto_max = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
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
                    table.PrimaryKey("pk_aprobadores_limites", x => x.id);
                    table.CheckConstraint("ck_aprobador_monto_positivo", "monto_max > 0");
                });

            migrationBuilder.CreateTable(
                name: "politicas_viaticos",
                schema: "cuentas_por_pagar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    puesto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_destino = table.Column<short>(type: "smallint", nullable: false),
                    monto_max_dia = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    dias_max = table.Column<int>(type: "integer", nullable: false),
                    moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_politicas_viaticos", x => x.id);
                    table.CheckConstraint("ck_politica_dias_positivo", "dias_max > 0");
                    table.CheckConstraint("ck_politica_monto_positivo", "monto_max_dia > 0");
                });

            migrationBuilder.CreateTable(
                name: "solicitudes_viaticos",
                schema: "cuentas_por_pagar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    empleado_id = table.Column<Guid>(type: "uuid", nullable: false),
                    puesto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    jefe_directo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    destino = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    tipo_destino = table.Column<short>(type: "smallint", nullable: false),
                    fecha_salida = table.Column<DateOnly>(type: "date", nullable: false),
                    fecha_regreso = table.Column<DateOnly>(type: "date", nullable: false),
                    moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    monto_solicitado = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    tope_politica_snapshot = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    excede_politica = table.Column<bool>(type: "boolean", nullable: false),
                    justificacion_exceso = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    autorizado_por_jefe = table.Column<Guid>(type: "uuid", nullable: true),
                    fecha_autorizacion_jefe = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    autorizado_por_df = table.Column<Guid>(type: "uuid", nullable: true),
                    fecha_autorizacion_df = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rechazado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    fecha_rechazo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    motivo_rechazo = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    fecha_anticipo_pagado = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    fecha_comprobacion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    fecha_liquidacion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    fecha_solicitud = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    monto_comprobado = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    diferencia_liquidacion = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_solicitudes_viaticos", x => x.id);
                    table.CheckConstraint("ck_via_monto_positivo", "monto_solicitado > 0");
                });

            migrationBuilder.CreateTable(
                name: "lineas_comprobacion_viaticos",
                schema: "cuentas_por_pagar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    solicitud_viaticos_id = table.Column<Guid>(type: "uuid", nullable: false),
                    factura_proveedor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cfdi_recibido_id = table.Column<Guid>(type: "uuid", nullable: true),
                    uuid_cfdi = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    proveedor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    folio_proveedor = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    fecha_gasto = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    subtotal = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    impuestos_trasladados = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    retenciones = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    concepto = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    es_ticket_no_fiscal = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lineas_comprobacion_viaticos", x => x.id);
                    table.CheckConstraint("ck_linea_via_total_positivo", "total > 0");
                    table.ForeignKey(
                        name: "fk_lineas_comprobacion_viaticos_solicitudes_viaticos_solicitud",
                        column: x => x.solicitud_viaticos_id,
                        principalSchema: "cuentas_por_pagar",
                        principalTable: "solicitudes_viaticos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_aprobadores_empleado_tipo",
                schema: "cuentas_por_pagar",
                table: "aprobadores_limites",
                columns: new[] { "empleado_id", "tipo_gasto", "vigencia_desde" });

            migrationBuilder.CreateIndex(
                name: "ix_linea_via_factura",
                schema: "cuentas_por_pagar",
                table: "lineas_comprobacion_viaticos",
                column: "factura_proveedor_id",
                filter: "factura_proveedor_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_lineas_comprobacion_viaticos_solicitud_viaticos_id",
                schema: "cuentas_por_pagar",
                table: "lineas_comprobacion_viaticos",
                column: "solicitud_viaticos_id");

            migrationBuilder.CreateIndex(
                name: "ux_politicas_viaticos_puesto_destino",
                schema: "cuentas_por_pagar",
                table: "politicas_viaticos",
                columns: new[] { "empresa_id", "puesto_id", "tipo_destino" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_via_empleado_estado",
                schema: "cuentas_por_pagar",
                table: "solicitudes_viaticos",
                columns: new[] { "empleado_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_via_jefe_estado",
                schema: "cuentas_por_pagar",
                table: "solicitudes_viaticos",
                columns: new[] { "jefe_directo_id", "estado" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "aprobadores_limites",
                schema: "cuentas_por_pagar");

            migrationBuilder.DropTable(
                name: "lineas_comprobacion_viaticos",
                schema: "cuentas_por_pagar");

            migrationBuilder.DropTable(
                name: "politicas_viaticos",
                schema: "cuentas_por_pagar");

            migrationBuilder.DropTable(
                name: "solicitudes_viaticos",
                schema: "cuentas_por_pagar");
        }
    }
}
