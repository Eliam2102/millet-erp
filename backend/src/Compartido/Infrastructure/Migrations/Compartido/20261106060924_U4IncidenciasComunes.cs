using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class U4IncidenciasComunes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "incidencias",
                schema: "compartido",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    modulo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    tipo_documento = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    documento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    documento_etiqueta = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    paso = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    codigo_motivo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    motivo = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    detalle = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    detalle_clave = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    responsable_usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    responsable_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    fecha_compromiso = table.Column<DateOnly>(type: "date", nullable: true),
                    abierta_por_id = table.Column<Guid>(type: "uuid", nullable: true),
                    abierta_por_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    automatica = table.Column<bool>(type: "boolean", nullable: false),
                    abierta_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    paso_reanudacion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ruta_reanudacion = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    resuelta_por_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resuelta_por_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    resuelta_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolucion = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
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
                    table.CheckConstraint("ck_incidencias_estado", "estado BETWEEN 1 AND 4");
                    table.CheckConstraint("ck_incidencias_motivo", "char_length(motivo) BETWEEN 5 AND 1000");
                    table.CheckConstraint("ck_incidencias_resuelta_coherente", "(estado = 3 AND resuelta_en IS NOT NULL AND resolucion IS NOT NULL) OR (estado <> 3 AND resuelta_en IS NULL)");
                });

            migrationBuilder.CreateTable(
                name: "incidencia_historial",
                schema: "compartido",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    incidencia_id = table.Column<Guid>(type: "uuid", nullable: false),
                    orden = table.Column<int>(type: "integer", nullable: false),
                    accion = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    estado_anterior = table.Column<short>(type: "smallint", nullable: true),
                    estado_nuevo = table.Column<short>(type: "smallint", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    usuario_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    nota = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_incidencia_historial", x => x.id);
                    table.ForeignKey(
                        name: "fk_incidencia_historial_incidencias_incidencia_id",
                        column: x => x.incidencia_id,
                        principalSchema: "compartido",
                        principalTable: "incidencias",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                columns: new[] { "id", "activo", "aplica_origen", "codigo", "created_at", "created_by", "deleted_at", "nombre", "obligatorio", "obligatorio_persona_fisica", "obligatorio_persona_moral", "orden", "solo_persona_moral", "tipo_entidad", "updated_at", "updated_by", "version", "vigencia_meses" },
                values: new object[] { new Guid("00000011-0008-0000-0000-000000000001"), true, (short)0, "evidencia_incidencia", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Evidencia de la incidencia", false, false, false, 1, false, "incidencia", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1, null });

            migrationBuilder.CreateIndex(
                name: "ix_incidencia_historial_incidencia_id_orden",
                schema: "compartido",
                table: "incidencia_historial",
                columns: new[] { "incidencia_id", "orden" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_incidencias_documento_id_paso",
                schema: "compartido",
                table: "incidencias",
                columns: new[] { "documento_id", "paso" });

            migrationBuilder.CreateIndex(
                name: "ix_incidencias_empresa_id_modulo_estado",
                schema: "compartido",
                table: "incidencias",
                columns: new[] { "empresa_id", "modulo", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_incidencias_numero",
                schema: "compartido",
                table: "incidencias",
                column: "numero",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_incidencias_responsable_usuario_id_estado",
                schema: "compartido",
                table: "incidencias",
                columns: new[] { "responsable_usuario_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ux_incidencias_pendiente",
                schema: "compartido",
                table: "incidencias",
                columns: new[] { "empresa_id", "paso", "documento_id", "detalle_clave" },
                unique: true,
                filter: "estado IN (1, 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "incidencia_historial",
                schema: "compartido");

            migrationBuilder.DropTable(
                name: "incidencias",
                schema: "compartido");

            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("00000011-0008-0000-0000-000000000001"));
        }
    }
}
