using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.ComercioExterior.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ComercioExteriorHitosAvisos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hitos",
                schema: "comercio_exterior",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expediente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    clave = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    orden = table.Column<int>(type: "integer", nullable: false),
                    fecha_compromiso = table.Column<DateOnly>(type: "date", nullable: true),
                    fecha_real = table.Column<DateOnly>(type: "date", nullable: true),
                    responsable_usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evidencia_adjunto_id = table.Column<Guid>(type: "uuid", nullable: true),
                    omitido = table.Column<bool>(type: "boolean", nullable: false),
                    motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    motivo_cambio_compromiso = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hitos", x => x.id);
                    table.ForeignKey(
                        name: "fk_hitos_expedientes_expediente_id",
                        column: x => x.expediente_id,
                        principalSchema: "comercio_exterior",
                        principalTable: "expedientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "avisos_hito_vencido",
                schema: "comercio_exterior",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expediente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hito_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha_compromiso = table.Column<DateOnly>(type: "date", nullable: false),
                    fecha_aviso = table.Column<DateOnly>(type: "date", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_avisos_hito_vencido", x => x.id);
                    table.ForeignKey(
                        name: "fk_avisos_hito_vencido_expedientes_expediente_id",
                        column: x => x.expediente_id,
                        principalSchema: "comercio_exterior",
                        principalTable: "expedientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_avisos_hito_vencido_hitos_hito_id",
                        column: x => x.hito_id,
                        principalSchema: "comercio_exterior",
                        principalTable: "hitos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_avisos_hito_vencido_expediente_id",
                schema: "comercio_exterior",
                table: "avisos_hito_vencido",
                column: "expediente_id");

            migrationBuilder.CreateIndex(
                name: "ix_avisos_hito_vencido_hito_id_fecha_compromiso",
                schema: "comercio_exterior",
                table: "avisos_hito_vencido",
                columns: new[] { "hito_id", "fecha_compromiso" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_hitos_expediente_id_clave",
                schema: "comercio_exterior",
                table: "hitos",
                columns: new[] { "expediente_id", "clave" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_hitos_expediente_id_orden",
                schema: "comercio_exterior",
                table: "hitos",
                columns: new[] { "expediente_id", "orden" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_hitos_fecha_compromiso",
                schema: "comercio_exterior",
                table: "hitos",
                column: "fecha_compromiso",
                filter: "fecha_real IS NULL AND NOT omitido");

            // CE-01a no guardaba hitos: la apertura es la evidencia histórica disponible
            // para Solicitado. Las aperturas nuevas leen la fecha documental de la OC por puerto.
            migrationBuilder.Sql("""
                WITH nuevos AS (
                    INSERT INTO comercio_exterior.hitos
                        (id, empresa_id, expediente_id, clave, orden, fecha_real, responsable_usuario_id,
                         omitido, version, created_at, updated_at, created_by, updated_by)
                    SELECT md5(e.id::text || '/CE-HITO/' || c.clave)::uuid, e.empresa_id, e.id, c.clave, c.orden,
                           CASE WHEN c.orden = 1 THEN (e.created_at AT TIME ZONE 'America/Mexico_City')::date END,
                           e.responsable_usuario_id, false, 1, e.created_at, e.updated_at,
                           'MigracionCE01b', 'MigracionCE01b'
                    FROM comercio_exterior.expedientes e
                    CROSS JOIN (VALUES
                        ('solicitado', 1), ('planeado', 2), ('booking-confirmado', 3), ('contenedor-asignado', 4),
                        ('documentacion-en-proceso', 5), ('documentacion-completa', 6), ('cargado', 7),
                        ('despacho-aduanal', 8), ('embarcado', 9), ('arribado', 10), ('liberado', 11),
                        ('en-entrega', 12), ('entregado', 13), ('gastos-pendientes', 14), ('cerrado', 15)
                    ) AS c(clave, orden)
                    ON CONFLICT (expediente_id, clave) DO NOTHING
                    RETURNING expediente_id
                )
                UPDATE comercio_exterior.expedientes e
                SET revision_contenido = e.revision_contenido + 1, version = e.version + 1
                WHERE e.id IN (SELECT expediente_id FROM nuevos);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "avisos_hito_vencido",
                schema: "comercio_exterior");

            migrationBuilder.DropTable(
                name: "hitos",
                schema: "comercio_exterior");
        }
    }
}
