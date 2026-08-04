using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NotasCreditoProveedor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notas_credito_proveedor",
                schema: "cuentas_por_pagar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cfdi_recibido_id = table.Column<Guid>(type: "uuid", nullable: true),
                    uuid_cfdi = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    proveedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    folio_proveedor = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    serie_proveedor = table.Column<string>(type: "character varying(25)", maxLength: 25, nullable: true),
                    fecha_cfdi = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    tipo_cambio = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    subtotal = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    impuestos_trasladados = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    retenciones = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    tipo = table.Column<short>(type: "smallint", nullable: false),
                    tipo_relacion_cfdi = table.Column<short>(type: "smallint", nullable: false),
                    uuid_relacion_cfdi = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    factura_origen_id = table.Column<Guid>(type: "uuid", nullable: true),
                    monto_aplicado = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    fecha_captura = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fecha_match = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("pk_notas_credito_proveedor", x => x.id);
                    table.CheckConstraint("ck_nc_monto_aplicado_no_excede_total", "monto_aplicado >= 0 AND monto_aplicado <= total");
                    table.CheckConstraint("ck_nc_total_positivo", "total > 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_nc_en_espera_match",
                schema: "cuentas_por_pagar",
                table: "notas_credito_proveedor",
                columns: new[] { "proveedor_id", "uuid_relacion_cfdi" },
                filter: "estado = 1");

            migrationBuilder.CreateIndex(
                name: "ix_nc_factura_origen",
                schema: "cuentas_por_pagar",
                table: "notas_credito_proveedor",
                column: "factura_origen_id",
                filter: "factura_origen_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_nc_proveedor_fecha",
                schema: "cuentas_por_pagar",
                table: "notas_credito_proveedor",
                columns: new[] { "proveedor_id", "fecha_cfdi" });

            migrationBuilder.CreateIndex(
                name: "ux_notas_credito_proveedor_uuid",
                schema: "cuentas_por_pagar",
                table: "notas_credito_proveedor",
                column: "uuid_cfdi",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notas_credito_proveedor",
                schema: "cuentas_por_pagar");
        }
    }
}
