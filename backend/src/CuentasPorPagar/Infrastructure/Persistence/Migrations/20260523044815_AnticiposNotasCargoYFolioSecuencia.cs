using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AnticiposNotasCargoYFolioSecuencia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "anticipos_proveedor",
                schema: "cuentas_por_pagar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cfdi_recibido_id = table.Column<Guid>(type: "uuid", nullable: true),
                    uuid_cfdi = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    proveedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    serie = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    folio_proveedor = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    fecha_cfdi = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    tipo_cambio = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    monto_entregado = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    monto_amortizado = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    orden_compra_id = table.Column<Guid>(type: "uuid", nullable: true),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    fecha_captura = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fecha_amortizacion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    fecha_cancelacion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    motivo_cancelacion = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    capturado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_anticipos_proveedor", x => x.id);
                    table.CheckConstraint("ck_anticipo_monto_amortizado_valido", "monto_amortizado >= 0 AND monto_amortizado <= monto_entregado");
                    table.CheckConstraint("ck_anticipo_monto_entregado_positivo", "monto_entregado > 0");
                });

            migrationBuilder.CreateTable(
                name: "folio_secuencias_nota_cargo",
                schema: "cuentas_por_pagar",
                columns: table => new
                {
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    anio = table.Column<short>(type: "smallint", nullable: false),
                    siguiente = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_folio_secuencias_nota_cargo", x => new { x.empresa_id, x.anio });
                });

            migrationBuilder.CreateTable(
                name: "notas_cargo",
                schema: "cuentas_por_pagar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    folio = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    folio_anio = table.Column<short>(type: "smallint", nullable: false),
                    proveedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    concepto = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    concepto_contable_id = table.Column<Guid>(type: "uuid", nullable: true),
                    monto = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    tipo_cambio = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    factura_origen_id = table.Column<Guid>(type: "uuid", nullable: true),
                    devolucion_a_proveedor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    nota_credito_proveedor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    autorizado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    aplicado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    fecha_creacion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fecha_autorizacion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    fecha_aplicacion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    fecha_formalizacion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    fecha_cancelacion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    motivo_cancelacion = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notas_cargo", x => x.id);
                    table.CheckConstraint("ck_ncg_monto_positivo", "monto > 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_anticipos_proveedor_estado",
                schema: "cuentas_por_pagar",
                table: "anticipos_proveedor",
                columns: new[] { "proveedor_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_anticipos_proveedor_oc",
                schema: "cuentas_por_pagar",
                table: "anticipos_proveedor",
                column: "orden_compra_id",
                filter: "orden_compra_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_anticipos_proveedor_uuid",
                schema: "cuentas_por_pagar",
                table: "anticipos_proveedor",
                column: "uuid_cfdi",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notas_cargo_factura_origen",
                schema: "cuentas_por_pagar",
                table: "notas_cargo",
                column: "factura_origen_id",
                filter: "factura_origen_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_notas_cargo_proveedor_estado",
                schema: "cuentas_por_pagar",
                table: "notas_cargo",
                columns: new[] { "proveedor_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ux_notas_cargo_folio",
                schema: "cuentas_por_pagar",
                table: "notas_cargo",
                columns: new[] { "empresa_id", "folio_anio", "folio" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "anticipos_proveedor",
                schema: "cuentas_por_pagar");

            migrationBuilder.DropTable(
                name: "folio_secuencias_nota_cargo",
                schema: "cuentas_por_pagar");

            migrationBuilder.DropTable(
                name: "notas_cargo",
                schema: "cuentas_por_pagar");
        }
    }
}
