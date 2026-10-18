using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Contabilidad.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class F1Con04PolizasNumeracionAnulacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "tipo_sat",
                schema: "contabilidad",
                table: "tipos_documento_contable",
                type: "smallint",
                nullable: false,
                defaultValue: (short)3); // Los tipos existentes quedan como póliza de diario.

            migrationBuilder.CreateTable(
                name: "consecutivos_poliza",
                schema: "contabilidad",
                columns: table => new
                {
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_operacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    anio = table.Column<int>(type: "integer", nullable: false),
                    ultimo_numero = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consecutivos_poliza", x => new { x.empresa_id, x.tipo_operacion_id, x.anio });
                    table.CheckConstraint("ck_consecutivos_poliza_numero", "ultimo_numero BETWEEN 1 AND 999999");
                    table.ForeignKey(
                        name: "fk_consecutivos_poliza_tipos_documento_tipo_operacion_id",
                        column: x => x.tipo_operacion_id,
                        principalSchema: "contabilidad",
                        principalTable: "tipos_documento_contable",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "polizas",
                schema: "contabilidad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_operacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_clave = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tipo_sat = table.Column<short>(type: "smallint", nullable: false),
                    numero = table.Column<int>(type: "integer", nullable: true),
                    fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    periodo_anio = table.Column<int>(type: "integer", nullable: false),
                    periodo_numero = table.Column<int>(type: "integer", nullable: false),
                    concepto = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    origen = table.Column<short>(type: "smallint", nullable: false),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    origen_modulo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    origen_tipo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    origen_documento_id = table.Column<Guid>(type: "uuid", nullable: true),
                    origen_folio = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    origen_uuid = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    llave_idempotencia = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    poliza_anulada_id = table.Column<Guid>(type: "uuid", nullable: true),
                    anulada_por_poliza_id = table.Column<Guid>(type: "uuid", nullable: true),
                    motivo_anulacion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    anulada_por = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    anulada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    capturada_por_id = table.Column<Guid>(type: "uuid", nullable: true),
                    capturada_por = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    autorizada_por_id = table.Column<Guid>(type: "uuid", nullable: true),
                    autorizada_por = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    autorizada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    total_cargos = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_abonos = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_polizas", x => x.id);
                    table.CheckConstraint("ck_polizas_autorizada_cuadra", "estado = 0 OR (total_cargos = total_abonos AND total_cargos <> 0)");
                    table.CheckConstraint("ck_polizas_estado", "estado BETWEEN 0 AND 2");
                    table.CheckConstraint("ck_polizas_numero", "(estado = 0 AND numero IS NULL) OR (estado <> 0 AND numero BETWEEN 1 AND 999999)");
                    table.CheckConstraint("ck_polizas_origen", "origen BETWEEN 0 AND 1");
                    table.CheckConstraint("ck_polizas_periodo", "periodo_numero BETWEEN 1 AND 13");
                    table.CheckConstraint("ck_polizas_tipo_sat", "tipo_sat BETWEEN 1 AND 3");
                    table.ForeignKey(
                        name: "fk_polizas_tipos_documento_tipo_operacion_id",
                        column: x => x.tipo_operacion_id,
                        principalSchema: "contabilidad",
                        principalTable: "tipos_documento_contable",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "polizas_partidas",
                schema: "contabilidad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    poliza_id = table.Column<Guid>(type: "uuid", nullable: false),
                    linea = table.Column<int>(type: "integer", nullable: false),
                    cuenta_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cuenta_codigo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    concepto = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    cargo = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    abono = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    tipo_cambio = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    importe_original = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dim1id = table.Column<Guid>(type: "uuid", nullable: true),
                    dim2id = table.Column<Guid>(type: "uuid", nullable: true),
                    dim3id = table.Column<Guid>(type: "uuid", nullable: true),
                    proyecto = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: true),
                    proveedor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cuenta_bancaria_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_polizas_partidas", x => x.id);
                    table.CheckConstraint("ck_polizas_partidas_tipo_cambio", "tipo_cambio > 0");
                    table.CheckConstraint("ck_polizas_partidas_un_lado", "(cargo = 0) <> (abono = 0)");
                    table.ForeignKey(
                        name: "fk_polizas_partidas_cuentas_contables_cuenta_id",
                        column: x => x.cuenta_id,
                        principalSchema: "contabilidad",
                        principalTable: "cuentas_contables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_polizas_partidas_polizas_poliza_id",
                        column: x => x.poliza_id,
                        principalSchema: "contabilidad",
                        principalTable: "polizas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_tipos_documento_tipo_sat",
                schema: "contabilidad",
                table: "tipos_documento_contable",
                sql: "tipo_sat BETWEEN 1 AND 3");

            migrationBuilder.CreateIndex(
                name: "ix_consecutivos_poliza_tipo_operacion_id",
                schema: "contabilidad",
                table: "consecutivos_poliza",
                column: "tipo_operacion_id");

            migrationBuilder.CreateIndex(
                name: "ix_polizas_documento_origen",
                schema: "contabilidad",
                table: "polizas",
                columns: new[] { "empresa_id", "origen_modulo", "origen_tipo", "origen_documento_id" },
                filter: "origen_documento_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_polizas_estado_fecha",
                schema: "contabilidad",
                table: "polizas",
                columns: new[] { "empresa_id", "estado", "fecha" });

            migrationBuilder.CreateIndex(
                name: "ix_polizas_periodo_fecha",
                schema: "contabilidad",
                table: "polizas",
                columns: new[] { "empresa_id", "periodo_anio", "periodo_numero", "fecha", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_polizas_tipo_operacion_id",
                schema: "contabilidad",
                table: "polizas",
                column: "tipo_operacion_id");

            migrationBuilder.CreateIndex(
                name: "ux_polizas_anulada",
                schema: "contabilidad",
                table: "polizas",
                column: "poliza_anulada_id",
                unique: true,
                filter: "poliza_anulada_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_polizas_folio",
                schema: "contabilidad",
                table: "polizas",
                columns: new[] { "empresa_id", "tipo_operacion_id", "periodo_anio", "numero" },
                unique: true,
                filter: "numero IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_polizas_llave_idempotencia",
                schema: "contabilidad",
                table: "polizas",
                columns: new[] { "empresa_id", "llave_idempotencia" },
                unique: true,
                filter: "llave_idempotencia IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_polizas_partidas_cuenta",
                schema: "contabilidad",
                table: "polizas_partidas",
                columns: new[] { "empresa_id", "cuenta_id" });

            migrationBuilder.CreateIndex(
                name: "ix_polizas_partidas_cuenta_id",
                schema: "contabilidad",
                table: "polizas_partidas",
                column: "cuenta_id");

            migrationBuilder.CreateIndex(
                name: "ux_polizas_partidas_linea",
                schema: "contabilidad",
                table: "polizas_partidas",
                columns: new[] { "poliza_id", "linea" },
                unique: true);

            // K10.9 (supuesto V39): los tipos de operación de SAP (Cuestionario 08, C-02/R5) con su tipo de póliza del SAT, para cada
            // empresa existente. AN es «anticipo» de clientes y de proveedores en SAP: queda uno solo, como diario, hasta que Laura lo
            // defina en la sesión contable. Laura puede agregar o desactivar tipos desde el catálogo; no se duplican si ya existen.
            migrationBuilder.Sql("""
                INSERT INTO contabilidad.tipos_documento_contable
                    (id, empresa_id, clave, nombre, estatus, es_prueba, tipo_sat, version, created_at, updated_at, created_by, updated_by, deleted_at)
                SELECT gen_random_uuid(), e.id, t.clave, t.nombre, 0, false, t.tipo_sat, 1,
                       now() AT TIME ZONE 'UTC', now() AT TIME ZONE 'UTC', 'migration-F1Con04', 'migration-F1Con04', NULL
                FROM compartido.empresas e
                CROSS JOIN (VALUES
                    ('TT', 'Factura de proveedores', 3),
                    ('PC', 'Nota de crédito de proveedores', 3),
                    ('RF', 'Factura de deudores', 3),
                    ('RC', 'Nota de crédito de clientes', 3),
                    ('AN', 'Anticipo', 3),
                    ('PR', 'Pago recibido', 1),
                    ('PP', 'Pago a proveedor', 2),
                    ('DP', 'Depósito', 1),
                    ('EM', 'Entrada de mercancías', 3),
                    ('SM', 'Salida de mercancías', 3),
                    ('IM', 'Traslado de mercancías', 3),
                    ('AC', 'Capitalización de activos fijos', 3),
                    ('AS', 'Asiento contable', 3)
                ) AS t(clave, nombre, tipo_sat)
                ON CONFLICT (empresa_id, clave) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM contabilidad.tipos_documento_contable t
                WHERE t.created_by = 'migration-F1Con04'
                  AND NOT EXISTS (SELECT 1 FROM contabilidad.reglas_dimension r WHERE r.tipo_documento_id = t.id)
                  AND NOT EXISTS (SELECT 1 FROM contabilidad.movimientos_dimension_prueba m WHERE m.tipo_documento_id = t.id)
                  AND NOT EXISTS (SELECT 1 FROM contabilidad.polizas p WHERE p.tipo_operacion_id = t.id);
                """);

            migrationBuilder.DropTable(
                name: "consecutivos_poliza",
                schema: "contabilidad");

            migrationBuilder.DropTable(
                name: "polizas_partidas",
                schema: "contabilidad");

            migrationBuilder.DropTable(
                name: "polizas",
                schema: "contabilidad");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tipos_documento_tipo_sat",
                schema: "contabilidad",
                table: "tipos_documento_contable");

            migrationBuilder.DropColumn(
                name: "tipo_sat",
                schema: "contabilidad",
                table: "tipos_documento_contable");
        }
    }
}
