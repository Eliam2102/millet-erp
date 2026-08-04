using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Integraciones.Fiscal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "integraciones_fiscal");

            migrationBuilder.CreateTable(
                name: "configuracion_pac",
                schema: "integraciones_fiscal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    proveedor = table.Column<short>(type: "smallint", nullable: false),
                    base_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    api_key_cifrado = table.Column<byte[]>(type: "bytea", nullable: false),
                    api_key_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    timeout_descarga_segundos = table.Column<int>(type: "integer", nullable: false),
                    timeout_consulta_segundos = table.Column<int>(type: "integer", nullable: false),
                    retry_count = table.Column<int>(type: "integer", nullable: false),
                    circuit_breaker_failures = table.Column<int>(type: "integer", nullable: false),
                    circuit_breaker_duracion_segundos = table.Column<int>(type: "integer", nullable: false),
                    descarga_intervalo_segundos = table.Column<int>(type: "integer", nullable: false),
                    descarga_backfill_horas = table.Column<int>(type: "integer", nullable: false),
                    refresh_intervalo_segundos = table.Column<int>(type: "integer", nullable: false),
                    refresh_batch_size = table.Column<int>(type: "integer", nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    ultima_rotacion_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ultima_test_conexion_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ultima_test_conexion_exitosa = table.Column<bool>(type: "boolean", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_configuracion_pac", x => x.id);
                    table.CheckConstraint("ck_configuracion_pac_proveedor", "proveedor BETWEEN 1 AND 1");
                });

            migrationBuilder.CreateTable(
                name: "integration_events_outbox",
                schema: "integraciones_fiscal",
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
                name: "rfcs_receptores",
                schema: "integraciones_fiscal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rfc = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    descarga_habilitada = table.Column<bool>(type: "boolean", nullable: false),
                    refresh_habilitada = table.Column<bool>(type: "boolean", nullable: false),
                    checkpoint_descarga_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rfcs_receptores", x => x.id);
                    table.CheckConstraint("ck_rfcs_receptores_longitud", "char_length(rfc) BETWEEN 12 AND 13");
                });

            migrationBuilder.CreateIndex(
                name: "ix_configuracion_pac_activo",
                schema: "integraciones_fiscal",
                table: "configuracion_pac",
                column: "activo",
                filter: "activo = true AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "uq_configuracion_pac_empresa_proveedor",
                schema: "integraciones_fiscal",
                table: "configuracion_pac",
                columns: new[] { "empresa_id", "proveedor" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_integration_events_outbox_integration_empresa_id",
                schema: "integraciones_fiscal",
                table: "integration_events_outbox",
                column: "integration_empresa_id");

            migrationBuilder.CreateIndex(
                name: "ix_integration_events_outbox_pending",
                schema: "integraciones_fiscal",
                table: "integration_events_outbox",
                column: "published_at",
                filter: "published_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_rfcs_receptores_empresa_descarga",
                schema: "integraciones_fiscal",
                table: "rfcs_receptores",
                columns: new[] { "empresa_id", "descarga_habilitada" },
                filter: "descarga_habilitada = true AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "uq_rfcs_receptores_empresa_rfc",
                schema: "integraciones_fiscal",
                table: "rfcs_receptores",
                columns: new[] { "empresa_id", "rfc" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "configuracion_pac",
                schema: "integraciones_fiscal");

            migrationBuilder.DropTable(
                name: "integration_events_outbox",
                schema: "integraciones_fiscal");

            migrationBuilder.DropTable(
                name: "rfcs_receptores",
                schema: "integraciones_fiscal");
        }
    }
}
