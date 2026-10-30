using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Contabilidad.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class K105MovimientosPuente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "movimientos_puente",
                schema: "contabilidad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    poliza_id = table.Column<Guid>(type: "uuid", nullable: false),
                    poliza_partida_id = table.Column<Guid>(type: "uuid", nullable: false),
                    poliza_folio = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    cuenta_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    proveedor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    orden_compra_id = table.Column<Guid>(type: "uuid", nullable: false),
                    orden_compra_folio = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    linea_oc_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tipo_documento = table.Column<short>(type: "smallint", nullable: false),
                    documento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    factura_origen_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cargo = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    abono = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    tipo_cambio = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    importe_original = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    es_reversa = table.Column<bool>(type: "boolean", nullable: false),
                    movimiento_revertido_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_movimientos_puente", x => x.id);
                    table.CheckConstraint("ck_movimientos_puente_reversa", "(es_reversa AND movimiento_revertido_id IS NOT NULL) OR (NOT es_reversa AND movimiento_revertido_id IS NULL)");
                    table.CheckConstraint("ck_movimientos_puente_tipo_cambio", "tipo_cambio > 0");
                    table.CheckConstraint("ck_movimientos_puente_un_lado", "cargo >= 0 AND abono >= 0 AND (cargo = 0) <> (abono = 0)");
                    table.ForeignKey(
                        name: "fk_movimientos_puente_cuentas_contables_cuenta_id",
                        column: x => x.cuenta_id,
                        principalSchema: "contabilidad",
                        principalTable: "cuentas_contables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_movimientos_puente_movimientos_puente_movimiento_revertido_",
                        column: x => x.movimiento_revertido_id,
                        principalSchema: "contabilidad",
                        principalTable: "movimientos_puente",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_movimientos_puente_polizas_poliza_id",
                        column: x => x.poliza_id,
                        principalSchema: "contabilidad",
                        principalTable: "polizas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_movimientos_puente_cuenta_id",
                schema: "contabilidad",
                table: "movimientos_puente",
                column: "cuenta_id");

            migrationBuilder.CreateIndex(
                name: "ix_movimientos_puente_documento",
                schema: "contabilidad",
                table: "movimientos_puente",
                columns: new[] { "empresa_id", "tipo_documento", "documento_id" });

            migrationBuilder.CreateIndex(
                name: "ix_movimientos_puente_fecha",
                schema: "contabilidad",
                table: "movimientos_puente",
                columns: new[] { "empresa_id", "fecha" });

            migrationBuilder.CreateIndex(
                name: "ix_movimientos_puente_movimiento_revertido_id",
                schema: "contabilidad",
                table: "movimientos_puente",
                column: "movimiento_revertido_id");

            migrationBuilder.CreateIndex(
                name: "ix_movimientos_puente_oc_fecha",
                schema: "contabilidad",
                table: "movimientos_puente",
                columns: new[] { "empresa_id", "orden_compra_id", "fecha" });

            migrationBuilder.CreateIndex(
                name: "ix_movimientos_puente_poliza",
                schema: "contabilidad",
                table: "movimientos_puente",
                column: "poliza_id");

            migrationBuilder.CreateIndex(
                name: "ux_movimientos_puente_partida",
                schema: "contabilidad",
                table: "movimientos_puente",
                columns: new[] { "poliza_partida_id", "es_reversa" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "movimientos_puente",
                schema: "contabilidad");
        }
    }
}
