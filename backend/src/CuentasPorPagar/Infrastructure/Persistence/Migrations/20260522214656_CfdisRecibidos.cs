using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CfdisRecibidos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cfdis_recibidos",
                schema: "cuentas_por_pagar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    uuid_cfdi = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    rfc_emisor = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    rfc_receptor = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    tipo = table.Column<short>(type: "smallint", nullable: false),
                    folio = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    serie = table.Column<string>(type: "character varying(25)", maxLength: 25, nullable: true),
                    fecha_cfdi = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    subtotal = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    impuestos_trasladados = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    retenciones = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    tipo_cambio = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    canal_origen = table.Column<short>(type: "smallint", nullable: false),
                    fecha_recepcion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    xml_blob_ref = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    pdf_blob_ref = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    xml_hash_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    documento_destino_id = table.Column<Guid>(type: "uuid", nullable: true),
                    motivo_descarte = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    cfdi_original_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cfdis_recibidos", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cfdis_proveedor_total",
                schema: "cuentas_por_pagar",
                table: "cfdis_recibidos",
                columns: new[] { "rfc_emisor", "total" },
                filter: "estado = 1");

            migrationBuilder.CreateIndex(
                name: "ix_cfdis_recibidos_empresa",
                schema: "cuentas_por_pagar",
                table: "cfdis_recibidos",
                column: "empresa_id");

            migrationBuilder.CreateIndex(
                name: "ix_cfdis_recibidos_estado_fecha",
                schema: "cuentas_por_pagar",
                table: "cfdis_recibidos",
                columns: new[] { "estado", "fecha_recepcion" },
                filter: "estado = 1");

            migrationBuilder.CreateIndex(
                name: "ux_cfdis_recibidos_uuid",
                schema: "cuentas_por_pagar",
                table: "cfdis_recibidos",
                column: "uuid_cfdi",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cfdis_recibidos",
                schema: "cuentas_por_pagar");
        }
    }
}
