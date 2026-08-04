using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EstadoCuentaTcYParser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // F7-PR5 / Hotfix pg-trgm: la extensión pg_trgm no está
            // allow-listed en Azure Database for PostgreSQL Flexible Server
            // por default — habilitarla requiere agregar 'PG_TRGM' al server
            // parameter `azure.extensions` vía bicep, lo cual es un cambio
            // de infra fuera del scope de este PR. Como el algoritmo de
            // match de F7-PR5 corre íntegramente en C# (volumen MVP ~100
            // movs/mes/TC), removemos el CREATE EXTENSION y los GIN trgm
            // indexes — quedan como PLATFORM-TODO para cuando migremos el
            // match a SQL (volumen alto).
            //
            // PLATFORM-TODO(<PgTrgmAllowList>): allow-listar pg_trgm en
            // bicep + migración separada que ejecute CREATE EXTENSION
            // pg_trgm + GIN indexes sobre merchant_normalizado de
            // movimientos_tarjeta_credito y estado_cuenta_tc_lineas_banco.

            migrationBuilder.CreateTable(
                name: "estados_cuenta_tc",
                schema: "cuentas_por_pagar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tarjeta_id = table.Column<Guid>(type: "uuid", nullable: false),
                    periodo_desde = table.Column<DateOnly>(type: "date", nullable: false),
                    periodo_hasta = table.Column<DateOnly>(type: "date", nullable: false),
                    fecha_corte = table.Column<DateOnly>(type: "date", nullable: false),
                    fecha_limite_pago = table.Column<DateOnly>(type: "date", nullable: false),
                    archivo_banco_blob_ref = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    archivo_banco_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    archivo_banco_cargado_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    archivo_banco_cargado_by = table.Column<Guid>(type: "uuid", nullable: true),
                    perfil_parser_usado = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    total_banco_mxn = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    total_conciliado_mxn = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    diferencia_mxn = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    factura_proveedor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    diferencia_cambiaria_mxn = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_estados_cuenta_tc", x => x.id);
                    table.CheckConstraint("ck_ec_periodo_coherente", "periodo_desde <= periodo_hasta");
                });

            migrationBuilder.CreateTable(
                name: "perfiles_parser_banco",
                schema: "cuentas_por_pagar",
                columns: table => new
                {
                    codigo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    formato_archivo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    encoding = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    fila_inicio_datos = table.Column<int>(type: "integer", nullable: false),
                    columna_fecha = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    formato_fecha = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    columna_monto = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    columna_moneda = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    columna_merchant = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    columna_referencia = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    columna_tipo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    regla_signo_refund = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    locale_montos = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_perfiles_parser_banco", x => x.codigo);
                });

            migrationBuilder.CreateTable(
                name: "estado_cuenta_tc_lineas_banco",
                schema: "cuentas_por_pagar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    estado_cuenta_tc_id = table.Column<Guid>(type: "uuid", nullable: false),
                    posicion_archivo = table.Column<int>(type: "integer", nullable: false),
                    fecha_aplicacion = table.Column<DateOnly>(type: "date", nullable: false),
                    monto = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    monto_mxn = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    merchant_raw = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    merchant_normalizado = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    referencia_banco = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    tipo_segun_banco = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    movimiento_tc_id = table.Column<Guid>(type: "uuid", nullable: true),
                    estado_match = table.Column<short>(type: "smallint", nullable: false),
                    score_match = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_estado_cuenta_tc_lineas_banco", x => x.id);
                    table.ForeignKey(
                        name: "fk_estado_cuenta_tc_lineas_banco_estados_cuenta_tc_estado_cuen",
                        column: x => x.estado_cuenta_tc_id,
                        principalSchema: "cuentas_por_pagar",
                        principalTable: "estados_cuenta_tc",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_linea_estado_match",
                schema: "cuentas_por_pagar",
                table: "estado_cuenta_tc_lineas_banco",
                columns: new[] { "estado_cuenta_tc_id", "estado_match" });

            migrationBuilder.CreateIndex(
                name: "ix_linea_movimiento",
                schema: "cuentas_por_pagar",
                table: "estado_cuenta_tc_lineas_banco",
                column: "movimiento_tc_id",
                filter: "movimiento_tc_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_linea_archivo_posicion",
                schema: "cuentas_por_pagar",
                table: "estado_cuenta_tc_lineas_banco",
                columns: new[] { "estado_cuenta_tc_id", "posicion_archivo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ec_tarjeta_estado",
                schema: "cuentas_por_pagar",
                table: "estados_cuenta_tc",
                columns: new[] { "tarjeta_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ux_ec_archivo_hash",
                schema: "cuentas_por_pagar",
                table: "estados_cuenta_tc",
                columns: new[] { "tarjeta_id", "archivo_banco_hash" },
                unique: true,
                filter: "archivo_banco_hash IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_ec_periodo_tarjeta",
                schema: "cuentas_por_pagar",
                table: "estados_cuenta_tc",
                columns: new[] { "tarjeta_id", "periodo_desde", "periodo_hasta" },
                unique: true);

            // F7-PR5 / Hotfix pg-trgm: GIN trigram indexes diferidos —
            // requieren la extensión pg_trgm que no está allow-listed en
            // Azure PG. El match corre en C# en F7-PR5; los indexes
            // ix_mov_tc_merchant_trgm / ix_lineas_banco_merchant_trgm se
            // crearán cuando se cierre PLATFORM-TODO(<PgTrgmAllowList>).
            // Por ahora el btree default sobre merchant_normalizado
            // (`ix_mov_tc_merchant_norm` del F7-PR4) cubre el lookup
            // por igualdad exacta.

            // F7-PR5: seed del perfil AMEX_MX (D8 — único banco MVP).
            // Otros bancos se agregan via INSERT cuando el área los
            // confirme. GUID determinista para idempotencia del seed
            // (mismo empresa_id en dev y prod — la migración corre con
            // el bypass activo del query filter, vía CurrentEmpresa.Bypass()).
            migrationBuilder.Sql(@"
                INSERT INTO cuentas_por_pagar.perfiles_parser_banco (
                    codigo, empresa_id, nombre, formato_archivo, encoding,
                    fila_inicio_datos, columna_fecha, formato_fecha, columna_monto,
                    columna_moneda, columna_merchant, columna_referencia,
                    columna_tipo, regla_signo_refund, locale_montos, activo
                ) VALUES (
                    'AMEX_MX',
                    '00000000-0000-0000-0000-000000000000',
                    'American Express México',
                    'XLSX', 'UTF-8',
                    2,           -- headers en fila 1
                    'A',         -- columna Fecha
                    'dd/MM/yyyy',
                    'D',         -- columna Cargo (monto en MXN)
                    NULL,        -- moneda asumida MXN (default tarjeta)
                    'B',         -- columna Descripción / Merchant
                    'C',         -- columna Referencia / Authorization
                    NULL,        -- sin columna tipo (se infiere por signo)
                    'NegativoEsRefund',
                    'es-MX',
                    true
                )
                ON CONFLICT (codigo) DO NOTHING;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "estado_cuenta_tc_lineas_banco",
                schema: "cuentas_por_pagar");

            migrationBuilder.DropTable(
                name: "perfiles_parser_banco",
                schema: "cuentas_por_pagar");

            migrationBuilder.DropTable(
                name: "estados_cuenta_tc",
                schema: "cuentas_por_pagar");
        }
    }
}
