using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class ExpedienteMatrizYExcepciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "solo_nacional",
                schema: "compartido",
                table: "adjunto_tipos_documento",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "adjunto_excepciones",
                schema: "compartido",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_entidad = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    entidad_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_documento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    otorgada_por_id = table.Column<Guid>(type: "uuid", nullable: true),
                    otorgada_por_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    otorgada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revocada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revocada_por_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_adjunto_excepciones", x => x.id);
                    table.CheckConstraint("ck_adjunto_excepciones_motivo", "char_length(motivo) BETWEEN 10 AND 500");
                    table.ForeignKey(
                        name: "fk_adjunto_excepciones_adjunto_tipos_documento_tipo_documento_",
                        column: x => x.tipo_documento_id,
                        principalSchema: "compartido",
                        principalTable: "adjunto_tipos_documento",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("00000011-0001-0000-0000-000000000001"),
                column: "solo_nacional",
                value: true);

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("00000011-0001-0000-0000-000000000002"),
                column: "solo_nacional",
                value: false);

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("00000011-0001-0000-0000-000000000003"),
                column: "solo_nacional",
                value: false);

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("00000011-0001-0000-0000-000000000004"),
                column: "solo_nacional",
                value: false);

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("00000011-0001-0000-0000-000000000005"),
                column: "solo_nacional",
                value: false);

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("00000011-0001-0000-0000-000000000006"),
                column: "solo_nacional",
                value: false);

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("00000011-0006-0000-0000-000000000001"),
                column: "solo_nacional",
                value: false);

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("00000011-0006-0000-0000-000000000002"),
                column: "solo_nacional",
                value: false);

            migrationBuilder.InsertData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                columns: new[] { "id", "activo", "codigo", "created_at", "created_by", "deleted_at", "nombre", "obligatorio", "orden", "solo_nacional", "solo_persona_moral", "tipo_entidad", "updated_at", "updated_by", "version", "vigencia_meses" },
                values: new object[,]
                {
                    { new Guid("00000011-0001-0000-0000-000000000007"), true, "opinion_cumplimiento", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Opinión de cumplimiento (32-D)", false, 7, true, false, "proveedor", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1, 1 },
                    { new Guid("00000011-0001-0000-0000-000000000008"), true, "fotografias_domicilio", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Fotografías del domicilio fiscal", false, 8, false, false, "proveedor", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1, null }
                });

            migrationBuilder.CreateIndex(
                name: "ix_adjunto_excepciones_tipo_documento_id",
                schema: "compartido",
                table: "adjunto_excepciones",
                column: "tipo_documento_id");

            migrationBuilder.CreateIndex(
                name: "ix_adjunto_excepciones_tipo_entidad_entidad_id",
                schema: "compartido",
                table: "adjunto_excepciones",
                columns: new[] { "tipo_entidad", "entidad_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "adjunto_excepciones",
                schema: "compartido");

            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("00000011-0001-0000-0000-000000000007"));

            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("00000011-0001-0000-0000-000000000008"));

            migrationBuilder.DropColumn(
                name: "solo_nacional",
                schema: "compartido",
                table: "adjunto_tipos_documento");
        }
    }
}
