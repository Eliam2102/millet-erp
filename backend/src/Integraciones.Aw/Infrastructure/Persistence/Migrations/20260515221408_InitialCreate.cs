using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Integraciones.Aw.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "integraciones_aw");

            migrationBuilder.CreateTable(
                name: "entidad_externa",
                schema: "integraciones_aw",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_entidad = table.Column<short>(type: "smallint", nullable: false),
                    referencia_externa = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payload_original = table.Column<string>(type: "jsonb", nullable: false),
                    payload_blob_id = table.Column<Guid>(type: "uuid", nullable: true),
                    edi_content = table.Column<string>(type: "text", nullable: true),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    delivered_to_aw_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    correlated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    aw_doc_id = table.Column<long>(type: "bigint", nullable: true),
                    aw_doc_id_secondary = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    submitted_by_spn_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    retry_count = table.Column<short>(type: "smallint", nullable: false),
                    resolution_note = table.Column<string>(type: "text", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entidad_externa", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "integration_events_outbox",
                schema: "integraciones_aw",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    integration_empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_integration_events_outbox", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "correlacion",
                schema: "integraciones_aw",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entidad_externa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    aw_doc_id = table.Column<long>(type: "bigint", nullable: false),
                    aw_doc_id_secondary = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    polling_cycle_number = table.Column<int>(type: "integer", nullable: false),
                    polling_query_duration_ms = table.Column<int>(type: "integer", nullable: true),
                    correlated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    aw_record_snapshot = table.Column<string>(type: "jsonb", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_correlacion", x => x.id);
                    table.ForeignKey(
                        name: "fk_correlacion_entidad_externa_entidad_externa_id",
                        column: x => x.entidad_externa_id,
                        principalSchema: "integraciones_aw",
                        principalTable: "entidad_externa",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "envio",
                schema: "integraciones_aw",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entidad_externa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_number = table.Column<short>(type: "smallint", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    drop_service_url = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    bytes_sent = table.Column<int>(type: "integer", nullable: true),
                    http_status_code = table.Column<short>(type: "smallint", nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    error_kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    duration_ms = table.Column<int>(type: "integer", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_envio", x => x.id);
                    table.ForeignKey(
                        name: "fk_envio_entidad_externa_entidad_externa_id",
                        column: x => x.entidad_externa_id,
                        principalSchema: "integraciones_aw",
                        principalTable: "entidad_externa",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "uq_correlacion_entidad_externa",
                schema: "integraciones_aw",
                table: "correlacion",
                column: "entidad_externa_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_entidad_externa_aw_doc",
                schema: "integraciones_aw",
                table: "entidad_externa",
                columns: new[] { "tipo_entidad", "aw_doc_id" },
                filter: "aw_doc_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_entidad_externa_correlation",
                schema: "integraciones_aw",
                table: "entidad_externa",
                columns: new[] { "tipo_entidad", "referencia_externa" },
                filter: "aw_doc_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_entidad_externa_empresa_id",
                schema: "integraciones_aw",
                table: "entidad_externa",
                column: "empresa_id");

            migrationBuilder.CreateIndex(
                name: "ix_entidad_externa_pendientes",
                schema: "integraciones_aw",
                table: "entidad_externa",
                columns: new[] { "estado", "submitted_at" },
                filter: "estado IN (0, 1, 3)");

            migrationBuilder.CreateIndex(
                name: "uq_entidad_externa_tipo_referencia_empresa",
                schema: "integraciones_aw",
                table: "entidad_externa",
                columns: new[] { "tipo_entidad", "referencia_externa", "empresa_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_envio_entidad_attempt",
                schema: "integraciones_aw",
                table: "envio",
                columns: new[] { "entidad_externa_id", "attempt_number" });

            migrationBuilder.CreateIndex(
                name: "ix_envio_failed_recent",
                schema: "integraciones_aw",
                table: "envio",
                column: "started_at",
                filter: "status = 2");

            migrationBuilder.CreateIndex(
                name: "ix_integration_events_outbox_integration_empresa_id",
                schema: "integraciones_aw",
                table: "integration_events_outbox",
                column: "integration_empresa_id");

            migrationBuilder.CreateIndex(
                name: "ix_integration_events_outbox_pending",
                schema: "integraciones_aw",
                table: "integration_events_outbox",
                column: "published_at",
                filter: "published_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "correlacion",
                schema: "integraciones_aw");

            migrationBuilder.DropTable(
                name: "envio",
                schema: "integraciones_aw");

            migrationBuilder.DropTable(
                name: "integration_events_outbox",
                schema: "integraciones_aw");

            migrationBuilder.DropTable(
                name: "entidad_externa",
                schema: "integraciones_aw");
        }
    }
}
