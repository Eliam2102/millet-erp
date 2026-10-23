using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.ComercioExterior.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ComercioExteriorEsquemaInicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "comercio_exterior");

            migrationBuilder.CreateSequence(
                name: "folio_seq",
                schema: "comercio_exterior");

            migrationBuilder.CreateTable(
                name: "expedientes",
                schema: "comercio_exterior",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    folio = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    tipo = table.Column<string>(type: "text", nullable: false),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    referencia = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    oc_id = table.Column<Guid>(type: "uuid", nullable: true),
                    incoterm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    tc = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    fecha_tc = table.Column<DateOnly>(type: "date", nullable: true),
                    fuente_tc = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    responsable_usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    estado = table.Column<string>(type: "text", nullable: false),
                    revision_pendiente = table.Column<bool>(type: "boolean", nullable: false),
                    revisado_por_id = table.Column<Guid>(type: "uuid", nullable: true),
                    motivo_cancelacion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    revision_contenido = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expedientes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "integration_events_outbox",
                schema: "comercio_exterior",
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
                name: "documentos",
                schema: "comercio_exterior",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expediente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_documento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero_version = table.Column<int>(type: "integer", nullable: false),
                    adjunto_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reemplaza_a = table.Column<Guid>(type: "uuid", nullable: true),
                    motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    no_aplica = table.Column<bool>(type: "boolean", nullable: false),
                    responsable_usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    estado = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documentos", x => x.id);
                    table.ForeignKey(
                        name: "fk_documentos_documentos_reemplaza_a",
                        column: x => x.reemplaza_a,
                        principalSchema: "comercio_exterior",
                        principalTable: "documentos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_documentos_expedientes_expediente_id",
                        column: x => x.expediente_id,
                        principalSchema: "comercio_exterior",
                        principalTable: "expedientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "participantes",
                schema: "comercio_exterior",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expediente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tipo = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    contacto = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    papel = table.Column<string>(type: "text", nullable: false),
                    proveedor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_participantes", x => x.id);
                    table.ForeignKey(
                        name: "fk_participantes_expedientes_expediente_id",
                        column: x => x.expediente_id,
                        principalSchema: "comercio_exterior",
                        principalTable: "expedientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_documentos_expediente_id_tipo_documento_id",
                schema: "comercio_exterior",
                table: "documentos",
                columns: new[] { "expediente_id", "tipo_documento_id" },
                unique: true,
                filter: "estado = 'Vigente'");

            migrationBuilder.CreateIndex(
                name: "ix_documentos_expediente_id_tipo_documento_id_numero_version",
                schema: "comercio_exterior",
                table: "documentos",
                columns: new[] { "expediente_id", "tipo_documento_id", "numero_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_documentos_reemplaza_a",
                schema: "comercio_exterior",
                table: "documentos",
                column: "reemplaza_a");

            migrationBuilder.CreateIndex(
                name: "ix_expedientes_empresa_id_folio",
                schema: "comercio_exterior",
                table: "expedientes",
                columns: new[] { "empresa_id", "folio" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_expedientes_empresa_id_sucursal_id_estado",
                schema: "comercio_exterior",
                table: "expedientes",
                columns: new[] { "empresa_id", "sucursal_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ux_ce_oc_abierta",
                schema: "comercio_exterior",
                table: "expedientes",
                columns: new[] { "empresa_id", "oc_id" },
                unique: true,
                filter: "oc_id IS NOT NULL AND estado = 'Abierto'");

            migrationBuilder.CreateIndex(
                name: "ix_integration_events_outbox_integration_empresa_id",
                schema: "comercio_exterior",
                table: "integration_events_outbox",
                column: "integration_empresa_id");

            migrationBuilder.CreateIndex(
                name: "ix_integration_events_outbox_pending",
                schema: "comercio_exterior",
                table: "integration_events_outbox",
                column: "published_at",
                filter: "published_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_participantes_expediente_id",
                schema: "comercio_exterior",
                table: "participantes",
                column: "expediente_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "documentos",
                schema: "comercio_exterior");

            migrationBuilder.DropTable(
                name: "integration_events_outbox",
                schema: "comercio_exterior");

            migrationBuilder.DropTable(
                name: "participantes",
                schema: "comercio_exterior");

            migrationBuilder.DropTable(
                name: "expedientes",
                schema: "comercio_exterior");

            migrationBuilder.DropSequence(
                name: "folio_seq",
                schema: "comercio_exterior");
        }
    }
}
