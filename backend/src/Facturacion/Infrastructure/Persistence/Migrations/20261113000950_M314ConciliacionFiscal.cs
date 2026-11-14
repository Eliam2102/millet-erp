using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M314ConciliacionFiscal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cierres_folios",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    anio = table.Column<int>(type: "integer", nullable: false),
                    mes = table.Column<int>(type: "integer", nullable: false),
                    tipo = table.Column<short>(type: "smallint", nullable: false),
                    serie = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    folio_inicial = table.Column<long>(type: "bigint", nullable: false),
                    folio_final = table.Column<long>(type: "bigint", nullable: false),
                    usados = table.Column<int>(type: "integer", nullable: false),
                    cancelados = table.Column<int>(type: "integer", nullable: false),
                    descartados = table.Column<int>(type: "integer", nullable: false),
                    reporte_json = table.Column<string>(type: "jsonb", nullable: false),
                    cerrado_por = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    cerrado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    vigente = table.Column<bool>(type: "boolean", nullable: false),
                    reabierto_por = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    reabierto_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    motivo_reapertura = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cierres_folios", x => x.id);
                    table.CheckConstraint("ck_cierres_folios_mes", "mes BETWEEN 1 AND 12");
                });

            migrationBuilder.CreateTable(
                name: "resoluciones_incidencia_fiscal",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    comprobante_id = table.Column<Guid>(type: "uuid", nullable: false),
                    uuid = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    estado_interno = table.Column<short>(type: "smallint", nullable: false),
                    estatus_sat_antes = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    estatus_sat_despues = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    soporte = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    nota = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    resuelta_por = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    resuelta_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_resoluciones_incidencia_fiscal", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_cierres_folios_vigente",
                schema: "facturacion",
                table: "cierres_folios",
                columns: new[] { "empresa_id", "anio", "mes", "tipo", "serie" },
                unique: true,
                filter: "vigente");

            migrationBuilder.CreateIndex(
                name: "ix_resoluciones_incidencia_fiscal_comprobante",
                schema: "facturacion",
                table: "resoluciones_incidencia_fiscal",
                columns: new[] { "empresa_id", "comprobante_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cierres_folios",
                schema: "facturacion");

            migrationBuilder.DropTable(
                name: "resoluciones_incidencia_fiscal",
                schema: "facturacion");
        }
    }
}
