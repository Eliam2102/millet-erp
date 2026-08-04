using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Integraciones.Fiscal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Pr9_DownloadRulesYSolicitudes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "download_rules_externas",
                schema: "integraciones_fiscal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rfc_receptor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_id_externo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    sat_query_type = table.Column<short>(type: "smallint", nullable: false),
                    download_type = table.Column<short>(type: "smallint", nullable: false),
                    sat_invoice_status = table.Column<short>(type: "smallint", nullable: false),
                    activa = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_download_rules_externas", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "solicitudes_descarga",
                schema: "integraciones_fiscal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    download_rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id_externo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    start_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    end_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    sat_request_status_externo = table.Column<int>(type: "integer", nullable: true),
                    download_request_status_externo = table.Column<int>(type: "integer", nullable: true),
                    invoice_count = table.Column<int>(type: "integer", nullable: true),
                    last_poll_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    next_poll_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    cosechada_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cerrada_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error_codigo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    error_mensaje = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    attempts_poll = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_solicitudes_descarga", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_download_rules_empresa_activa",
                schema: "integraciones_fiscal",
                table: "download_rules_externas",
                columns: new[] { "empresa_id", "activa" },
                filter: "activa = true AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "uq_download_rules_combo",
                schema: "integraciones_fiscal",
                table: "download_rules_externas",
                columns: new[] { "rfc_receptor_id", "sat_query_type", "download_type", "sat_invoice_status" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_solicitudes_descarga_next_poll",
                schema: "integraciones_fiscal",
                table: "solicitudes_descarga",
                columns: new[] { "estado", "next_poll_at" },
                filter: "estado IN (2, 3, 4) AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_solicitudes_empresa_rule_start",
                schema: "integraciones_fiscal",
                table: "solicitudes_descarga",
                columns: new[] { "empresa_id", "download_rule_id", "start_date" });

            migrationBuilder.CreateIndex(
                name: "uq_solicitudes_request_externo",
                schema: "integraciones_fiscal",
                table: "solicitudes_descarga",
                column: "request_id_externo",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "download_rules_externas",
                schema: "integraciones_fiscal");

            migrationBuilder.DropTable(
                name: "solicitudes_descarga",
                schema: "integraciones_fiscal");
        }
    }
}
