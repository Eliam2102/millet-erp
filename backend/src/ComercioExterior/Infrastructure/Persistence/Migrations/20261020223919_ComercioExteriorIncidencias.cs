using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.ComercioExterior.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ComercioExteriorIncidencias : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "numero_pedimento_vigente_importacion",
                schema: "comercio_exterior",
                table: "expedientes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "incidencias",
                schema: "comercio_exterior",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expediente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    responsable_usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    participante_id = table.Column<Guid>(type: "uuid", nullable: true),
                    motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    documentos_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    evidencias_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    accion_correctiva = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    efecto_posterior = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    hito_clave = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    contenedor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    partida_id = table.Column<Guid>(type: "uuid", nullable: true),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    pedimento_anterior = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    pedimento_nuevo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    motivo_cancelacion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_incidencias", x => x.id);
                    table.ForeignKey(
                        name: "fk_incidencias_expedientes_expediente_id",
                        column: x => x.expediente_id,
                        principalSchema: "comercio_exterior",
                        principalTable: "expedientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "avisos_incidencia",
                schema: "comercio_exterior",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expediente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    incidencia_id = table.Column<Guid>(type: "uuid", nullable: false),
                    destinatario_usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_avisos_incidencia", x => x.id);
                    table.ForeignKey(
                        name: "fk_avisos_incidencia_expedientes_expediente_id",
                        column: x => x.expediente_id,
                        principalSchema: "comercio_exterior",
                        principalTable: "expedientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_avisos_incidencia_incidencias_incidencia_id",
                        column: x => x.incidencia_id,
                        principalSchema: "comercio_exterior",
                        principalTable: "incidencias",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "incidencias_historial",
                schema: "comercio_exterior",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expediente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    incidencia_id = table.Column<Guid>(type: "uuid", nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    motivo = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_incidencias_historial", x => x.id);
                    table.ForeignKey(
                        name: "fk_incidencias_historial_incidencias_incidencia_id",
                        column: x => x.incidencia_id,
                        principalSchema: "comercio_exterior",
                        principalTable: "incidencias",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_avisos_incidencia_expediente_id",
                schema: "comercio_exterior",
                table: "avisos_incidencia",
                column: "expediente_id");

            migrationBuilder.CreateIndex(
                name: "ix_avisos_incidencia_incidencia_id",
                schema: "comercio_exterior",
                table: "avisos_incidencia",
                column: "incidencia_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_incidencias_empresa_id_estado_fecha",
                schema: "comercio_exterior",
                table: "incidencias",
                columns: new[] { "empresa_id", "estado", "fecha" });

            migrationBuilder.CreateIndex(
                name: "ix_incidencias_expediente_id",
                schema: "comercio_exterior",
                table: "incidencias",
                column: "expediente_id");

            migrationBuilder.CreateIndex(
                name: "ix_incidencias_historial_incidencia_id",
                schema: "comercio_exterior",
                table: "incidencias_historial",
                column: "incidencia_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "avisos_incidencia",
                schema: "comercio_exterior");

            migrationBuilder.DropTable(
                name: "incidencias_historial",
                schema: "comercio_exterior");

            migrationBuilder.DropTable(
                name: "incidencias",
                schema: "comercio_exterior");

            migrationBuilder.DropColumn(
                name: "numero_pedimento_vigente_importacion",
                schema: "comercio_exterior",
                table: "expedientes");
        }
    }
}
