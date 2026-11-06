using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Contabilidad.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class C16ReclasificacionIva : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "comprobante_id",
                schema: "contabilidad",
                table: "cobros_efectivo_caja",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "saldos_iva_documento",
                schema: "contabilidad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lado = table.Column<short>(type: "smallint", nullable: false),
                    origen_modulo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    origen_tipo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    documento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    folio = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    uuid = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    metodo_pago = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tercero_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    tipo_cambio = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    @base = table.Column<decimal>(name: "base", type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    iva = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    iva_retenido = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    aplicado = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    iva_reclasificado = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    iva_retenido_reclasificado = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    poliza_id = table.Column<Guid>(type: "uuid", nullable: false),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_saldos_iva_documento", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "reclasificaciones_iva",
                schema: "contabilidad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    saldo_iva_documento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<short>(type: "smallint", nullable: false),
                    pago_modulo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    pago_tipo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    pago_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    importe = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    iva = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    iva_retenido = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    iva_mxn = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    iva_retenido_mxn = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    poliza_id = table.Column<Guid>(type: "uuid", nullable: false),
                    anulada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reclasificaciones_iva", x => x.id);
                    table.ForeignKey(
                        name: "fk_reclasificaciones_iva_saldos_iva_documento_saldo_iva_docume",
                        column: x => x.saldo_iva_documento_id,
                        principalSchema: "contabilidad",
                        principalTable: "saldos_iva_documento",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_reclasificaciones_iva_fecha",
                schema: "contabilidad",
                table: "reclasificaciones_iva",
                columns: new[] { "empresa_id", "fecha" });

            migrationBuilder.CreateIndex(
                name: "ix_reclasificaciones_iva_saldo_iva_documento_id",
                schema: "contabilidad",
                table: "reclasificaciones_iva",
                column: "saldo_iva_documento_id");

            migrationBuilder.CreateIndex(
                name: "ux_reclasificaciones_iva_pago_documento",
                schema: "contabilidad",
                table: "reclasificaciones_iva",
                columns: new[] { "empresa_id", "pago_modulo", "pago_tipo", "pago_id", "saldo_iva_documento_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_saldos_iva_documento_lado_fecha",
                schema: "contabilidad",
                table: "saldos_iva_documento",
                columns: new[] { "empresa_id", "lado", "fecha" });

            migrationBuilder.CreateIndex(
                name: "ux_saldos_iva_documento_documento",
                schema: "contabilidad",
                table: "saldos_iva_documento",
                columns: new[] { "empresa_id", "origen_modulo", "origen_tipo", "documento_id" },
                unique: true);

            // C1.6 y K10.7 (86e3jvvm9): roles nuevos SIN cuenta (Contabilidad los mapea) y plantillas v2 de #5, #11a, #11b y #12:
            // la versión vigente más dos líneas sobre el campo IvaReclasificado (IVA pendiente → pagado / no cobrado → cobrado).
            // Solo se agrega si la vigente aún no lee IvaReclasificado (no pisa una versión que Contabilidad ya haya editado).
            migrationBuilder.Sql("""
                INSERT INTO contabilidad.roles_cuenta
                    (id, empresa_id, codigo, descripcion, cuenta_id, sucursal_id, moneda, vigente_desde, vigente_hasta,
                     version, created_at, updated_at, created_by, updated_by, deleted_at)
                SELECT gen_random_uuid(), e.id, r.codigo, r.descripcion, NULL, NULL, NULL, DATE '2000-01-01', NULL,
                       1, now() AT TIME ZONE 'UTC', now() AT TIME ZONE 'UTC', 'migration-C16', 'migration-C16', NULL
                FROM compartido.empresas e
                CROSS JOIN (VALUES
                    ('IVA_TRASLADADO_COBRADO', 'IVA trasladado cobrado (C1.6)'),
                    ('IVA_POR_PAGAR', 'IVA a cargo del mes por pagar (K10.7)'),
                    ('IVA_A_FAVOR', 'IVA a favor del mes (K10.7)')) AS r(codigo, descripcion)
                WHERE NOT EXISTS (SELECT 1 FROM contabilidad.roles_cuenta x WHERE x.empresa_id = e.id AND x.codigo = r.codigo);

                INSERT INTO contabilidad.plantillas_contables
                    (id, empresa_id, tipo_evento, numero_version, vigente_desde, tipo_operacion_clave, concepto, descripcion, lineas,
                     version, created_at, updated_at, created_by, updated_by, deleted_at)
                SELECT gen_random_uuid(), v.empresa_id, v.tipo_evento, v.numero_version + 1, v.vigente_desde, v.tipo_operacion_clave, v.concepto,
                       left(coalesce(v.descripcion, '') || ' · C1.6: reclasificación del IVA al pagar o cobrar', 500),
                       v.lineas || jsonb_build_array(
                           jsonb_build_object('Orden', jsonb_array_length(v.lineas) + 1, 'Rol', t.cargo, 'Lado', 'Cargo', 'Campo', 'IvaReclasificado',
                               'Auxiliar', 'Ninguno', 'PorLinea', false, 'SoloServicios', NULL, 'ConCentroCosto', false, 'Concepto', t.concepto_cargo),
                           jsonb_build_object('Orden', jsonb_array_length(v.lineas) + 2, 'Rol', t.abono, 'Lado', 'Abono', 'Campo', 'IvaReclasificado',
                               'Auxiliar', 'Ninguno', 'PorLinea', false, 'SoloServicios', NULL, 'ConCentroCosto', false, 'Concepto', t.concepto_abono)),
                       1, now() AT TIME ZONE 'UTC', now() AT TIME ZONE 'UTC', 'migration-C16', 'migration-C16', NULL
                FROM (VALUES
                    ('tesoreria.pago-factura-proveedor.aplicado.v1', 'IVA_ACREDITABLE_PAGADO', 'IVA acreditable pagado', 'IVA_ACREDITABLE_PENDIENTE', 'IVA acreditable pendiente'),
                    ('tesoreria.pago-cliente.confirmado.v1', 'IVA_TRASLADADO_NO_COBRADO', 'IVA trasladado no cobrado', 'IVA_TRASLADADO_COBRADO', 'IVA trasladado cobrado'),
                    ('facturacion.cobro-mostrador.registrado.v1', 'IVA_TRASLADADO_NO_COBRADO', 'IVA trasladado no cobrado', 'IVA_TRASLADADO_COBRADO', 'IVA trasladado cobrado'),
                    ('facturacion.caja-sesion.cerrada.v1', 'IVA_TRASLADADO_NO_COBRADO', 'IVA trasladado no cobrado', 'IVA_TRASLADADO_COBRADO', 'IVA trasladado cobrado'))
                    AS t(tipo_evento, cargo, concepto_cargo, abono, concepto_abono)
                JOIN (SELECT DISTINCT ON (empresa_id, tipo_evento) *
                      FROM contabilidad.plantillas_contables
                      WHERE deleted_at IS NULL
                      ORDER BY empresa_id, tipo_evento, numero_version DESC) v ON v.tipo_evento = t.tipo_evento
                WHERE v.lineas::text NOT LIKE '%IvaReclasificado%';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM contabilidad.plantillas_contables WHERE created_by = 'migration-C16';
                DELETE FROM contabilidad.roles_cuenta WHERE created_by = 'migration-C16' AND cuenta_id IS NULL;
                """);

            migrationBuilder.DropTable(
                name: "reclasificaciones_iva",
                schema: "contabilidad");

            migrationBuilder.DropTable(
                name: "saldos_iva_documento",
                schema: "contabilidad");

            migrationBuilder.DropColumn(
                name: "comprobante_id",
                schema: "contabilidad",
                table: "cobros_efectivo_caja");
        }
    }
}
